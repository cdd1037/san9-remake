using San9.Importer.San9PkTc101.Packages;
using San9.Content.Packages;
using San9.Content.Images;
using System.Diagnostics;
using San9.Content.Maps;
using San9.Importer.San9PkTc101;
using San9.Rendering;

try
{
    if(args.Length==0)throw new ArgumentException("Commands: import-text --source-exe PATH --data-dir PATH --output PATH [--exe-evidence PATH]; import-client --source-exe PATH --data-dir PATH --scenario PATH --output PATH; import-map --source-exe PATH --data-dir PATH --scenario PATH --output PATH; render-map --package PATH --season spring --output PATH [--x 0 --y 0 --width 1024 --height 768]");
    Dictionary<string,string> options=[];
    for(int i=1;i<args.Length;i+=2)
        if(i+1>=args.Length||!args[i].StartsWith("--",StringComparison.Ordinal)||!options.TryAdd(args[i],args[i+1]))throw new ArgumentException("Invalid or duplicate option.");
    string[] allowed=args[0] == "import-text" ? ["--source-exe","--data-dir","--output","--exe-evidence"] : args[0] is "import-map" or "import-client"?["--source-exe","--data-dir","--scenario","--output"]:["--package","--content-package","--season","--output","--x","--y","--width","--height","--terrain-only"];
    if(options.Keys.Any(k=>!allowed.Contains(k)))throw new ArgumentException("Unknown option.");
    string Required(string key)=>options.TryGetValue(key,out var value)?value:throw new ArgumentException($"Missing {key}.");
    var watch=Stopwatch.StartNew();
    if(args[0]=="import-text")
    {
        var identity=await San9.Importer.San9PkTc101.San9PkTc101Detector.DetectAsync(Required("--source-exe"));
        if(!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        San9.Importer.San9PkTc101.Text.MessageArchiveCompiler.Export(Required("--data-dir"), Required("--output"), Console.WriteLine);
        MessageVerification.Run(Required("--output"));
        if(options.TryGetValue("--exe-evidence",out var evidence))
            San9.Importer.San9PkTc101.Text.ExecutableTextCompiler.Export(Required("--source-exe"),evidence,Required("--output"));
    }
    else if(args[0]=="import-map")
    {
        var map=await MapImporter.ImportAsync(Required("--source-exe"),Required("--data-dir"),Required("--scenario"),Required("--output"),Console.WriteLine);
        Console.WriteLine($"Complete: {map.ScenarioId}, {watch.Elapsed.TotalSeconds:F2}s");
    }
    else if(args[0]=="import-client")
    {
        var content=await ClientContentImporter.ImportAsync(Required("--source-exe"),Required("--data-dir"),Required("--scenario"),Required("--output"),Console.WriteLine);
        Console.WriteLine($"Complete: {content.Scenario.ScenarioId}, {watch.Elapsed.TotalSeconds:F2}s");
    }
    else if(args[0]=="render-map")
    {
        if (options.ContainsKey("--package") == options.ContainsKey("--content-package")) throw new ArgumentException("Specify exactly one of --package or --content-package.");
        string root=options.ContainsKey("--content-package") ? Required("--content-package") : Required("--package");
        var content=options.ContainsKey("--content-package") ? await ClientContentFiles.LoadAsync(root) : null;
        var map=content?.Map ?? await MapPackageFiles.LoadAsync(root);
        if(content is null && !options.ContainsKey("--terrain-only")) throw new ArgumentException("Building rendering requires --content-package; a map catalog contains no runtime state.");
        var buildings=content is null ? Array.Empty<MapBuilding>() : BuildingVisuals.Resolve(map,content.Scenario.Settlements.Select(s =>
        {
            var force=s.OwnerId is int owner?content.Scenario.Forces.Single(f=>f.Id==owner):null;
            return new BuildingVisualState(s.Id,s.Durability,s.Appearance,Math.Max(s.Commerce,s.Cultivation),s.Soldiers,force?.ColorCode??50,force?.FlagGlyph??-1);
        }).ToArray()).Buildings;
        string season=options.GetValueOrDefault("--season",map.InitialSeason);
        var common=ImageAssetFiles.Load(root,map.Assets);
        var land=ImageAssetFiles.Load(root,map.Seasons.Single(s=>s.Id==season).Textures).ToDictionary(k=>int.Parse(k.Key),v=>v.Value);
        var renderer=new TerrainRenderer(map,common,land);
        int Number(string key,int fallback)=>options.TryGetValue(key,out var v)?int.Parse(v):fallback;
        int x=Number("--x",0),y=Number("--y",0),width=Number("--width",1024),height=Number("--height",768);
        if(x<0||y<0||width<=0||height<=0||(long)x+width>map.CanvasWidth||(long)y+height>map.CanvasHeight)throw new ArgumentException("Render region outside canvas.");
        byte[] pixels=new byte[checked(width*height*3)];
        for(int row=y/512;row<=(y+height-1)/512;row++)for(int col=x/512;col<=(x+width-1)/512;col++)
        {
            var chunk=renderer.RenderChunk(col,row);
            for(int py=Math.Max(y,row*512);py<Math.Min(y+height,row*512+chunk.Height);py++)
            {
                int fromX=Math.Max(x,col*512),toX=Math.Min(x+width,col*512+chunk.Width);
                chunk.Pixels.AsSpan(((py-row*512)*chunk.Width+fromX-col*512)*3,(toX-fromX)*3).CopyTo(pixels.AsSpan(((py-y)*width+fromX-x)*3));
            }
        }
        if(!options.ContainsKey("--terrain-only"))
        foreach(var sprite in map.Bridges.Concat(buildings.SelectMany(b=>b.Sprites)).Concat(map.Clouds))
        {
            var image=common[sprite.AssetId];
            for(int py=Math.Max(y,sprite.Y);py<Math.Min(y+height,sprite.Y+image.Height);py++)
            for(int px=Math.Max(x,sprite.X);px<Math.Min(x+width,sprite.X+image.Width);px++)
            {
                int from=((py-sprite.Y)*image.Width+px-sprite.X)*image.Channels,to=((py-y)*width+px-x)*3;
                int alpha=image.Channels==4?image.Pixels[from+3]:255;
                for(int c=0;c<3;c++)pixels[to+c]=(byte)((image.Pixels[from+c]*alpha+pixels[to+c]*(255-alpha)+127)/255);
            }
        }
        string path=Path.GetFullPath(Required("--output"));Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,new PngImage(width,height,3,pixels).Encode());
        Console.WriteLine($"Rendered {width}x{height} {season}, {watch.Elapsed.TotalSeconds:F2}s, peak working set {Process.GetCurrentProcess().PeakWorkingSet64/1048576} MiB");
    }
    else throw new ArgumentException("Unknown command.");
    return 0;
}
catch(Exception e)when(e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(e.Message);return 1;
}
