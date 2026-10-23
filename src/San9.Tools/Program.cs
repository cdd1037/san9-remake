using San9.Importer.San9PkTc101.Packages;
using San9.Content.Packages;
using San9.Content.Images;
using System.Diagnostics;
using San9.Content.Maps;
using San9.Importer.San9PkTc101;
using San9.Rendering;

try
{
    if(args.Length==0)throw new ArgumentException("Commands: import-content --game-dir PATH --output PATH [--rebuild opening]; verify-content --content-root PATH; import-textures --game-dir PATH --output PATH [--rebuild all|cursors|skin|map|portraits|library/ARCHIVE]; import-client --game-dir PATH --scenario FILENAME --texture-package PATH --output PATH [--message-reports true|false]; verify-textures --texture-package PATH; verify-client --content-package PATH; import-text --game-dir PATH --output PATH; render-map --content-package PATH --output PATH");
    Dictionary<string,string> options=[];
    for(int i=1;i<args.Length;i+=2)
        if(i+1>=args.Length||!args[i].StartsWith("--",StringComparison.Ordinal)||!options.TryAdd(args[i],args[i+1]))throw new ArgumentException("Invalid or duplicate option.");
    string[] allowed=args[0] == "import-content" ? ["--game-dir", "--output", "--rebuild"] : args[0] == "verify-content" ? ["--content-root"] : args[0] == "import-textures" ? ["--game-dir","--output","--rebuild"] : args[0] == "verify-textures" ? ["--texture-package"] : args[0] == "verify-client" ? ["--content-package"] : args[0] == "import-text" ? ["--game-dir","--output","--exe-evidence"] : args[0] == "import-client"?["--game-dir","--scenario","--texture-package","--output","--message-reports"]:["--content-package","--season","--output","--x","--y","--width","--height","--terrain-only"];
    if(options.Keys.Any(k=>!allowed.Contains(k)))throw new ArgumentException("Unknown option.");
    string Required(string key)=>options.TryGetValue(key,out var value)?value:throw new ArgumentException($"Missing {key}.");
    var watch=Stopwatch.StartNew();
    if (args[0] == "import-content")
    {
        if (options.TryGetValue("--rebuild", out var unit) && unit != "opening") throw new ArgumentException("Content rebuild unit must be opening.");
        await ContentCatalogImporter.ImportAsync(Required("--game-dir"), Required("--output"), Console.WriteLine, openingOnly: unit == "opening");
    }
    else if (args[0] == "verify-content")
    {
        string root = Required("--content-root");
        var catalog = await ContentCatalogFiles.LoadAsync(root);
        var textures = await TexturePackageFiles.LoadAsync(PackageFiles.Resolve(root, catalog.Textures));
        var opening = await ContentCatalogFiles.LoadOpeningAsync(root, catalog);
        opening.Menu.Validate(textures.Assets); opening.Selection.Validate(textures.Assets);
        foreach (var entry in catalog.Scenarios)
        {
            var scenario = await ScenarioContentFiles.LoadAsync(PackageFiles.Resolve(root, entry.Package));
            _ = ClientContentFiles.Resolve(scenario, textures, PackageFiles.Resolve(root, catalog.Textures));
            var preview = opening.Selection.Entries.Single(p => p.Id == entry.Id);
            if (scenario.Scenario.Year != preview.Year || scenario.Scenario.Month != preview.Month)
                throw new InvalidDataException($"Scenario preview date differs from world: {entry.Id}");
            var forces = San9.Application.Presentation.ScenarioForceSelection.Read(
                scenario.Scenario, scenario.Text, scenario.IllustrationRoles);
            if (preview.CityColors.Any(p => !forces.CityColors.TryGetValue(p.Key, out var color) || color != p.Value))
                throw new InvalidDataException($"Scenario preview ownership differs from world: {entry.Id}; rebuild opening content.");
            foreach (string path in scenario.Presentation.Sounds.Values)
                if (!File.Exists(PackageFiles.Resolve(scenario.ResourceRoot, path))) throw new InvalidDataException($"Missing sound: {path}");
            var session = San9.Application.GameSession.Start(scenario.Scenario, 42);
            if (!session.Snapshot.Forces.Select(f => f.Id).Order().SequenceEqual(scenario.Scenario.Forces.Select(f => f.Id).Order()) ||
                !session.Snapshot.Settlements.Select(s => s.Id).Order().SequenceEqual(scenario.Scenario.Settlements.Select(s => s.Id).Order()) ||
                !session.Snapshot.Officers.Select(o => o.Id).Order().SequenceEqual(scenario.Scenario.Officers.Select(o => o.State.Id).Order()))
                throw new InvalidDataException($"Initial world lost scenario identities: {entry.Id}");
            NewGameContentVerification.Verify(scenario);
            var query = new San9.Application.Presentation.InformationQuery(session, scenario.Text);
            foreach (var category in new[] { San9.Application.Presentation.InformationCategory.Forces, San9.Application.Presentation.InformationCategory.Cities,
                San9.Application.Presentation.InformationCategory.Regions, San9.Application.Presentation.InformationCategory.Officers, San9.Application.Presentation.InformationCategory.Treasures })
                _ = query.Read(category);
            Console.WriteLine($"Verified {entry.Id}: {scenario.Scenario.Forces.Length} forces; {scenario.Scenario.Settlements.Length} settlements; {scenario.Scenario.Officers.Length} officers; world and queries ready.");
        }
        await ImageAssetFiles.VerifyAsync(PackageFiles.Resolve(root, catalog.Textures), textures.Assets);
    }
    else if(args[0]=="verify-client")
    {
        string root = Required("--content-package");
        var package = await ClientContentFiles.LoadAsync(root);
        var assets = package.Map.Assets.Concat(package.Map.Seasons.SelectMany(s => s.Textures)).Concat(package.Skin.Assets).Concat(package.Textures.Assets).Distinct().ToArray();
        await ImageAssetFiles.VerifyAsync(package.TextureRoot, assets);
        Console.WriteLine($"Verified {assets.Length} logical images, {assets.Select(a => a.File).Distinct().Count()} physical files, formats and regions.");
    }
    else if(args[0]=="import-text")
    {
        var source = new San9SourceDirectory(Required("--game-dir"));
        var identity=await San9PkTc101Detector.DetectAsync(source.Executable);
        if(!identity.IsMatch) throw new InvalidDataException(identity.Reason);
        San9.Importer.San9PkTc101.Text.MessageArchiveCompiler.Export(source.Root, Required("--output"), Console.WriteLine);
        MessageVerification.Run(Required("--output"));
        if(options.TryGetValue("--exe-evidence",out var evidence))
            San9.Importer.San9PkTc101.Text.ExecutableTextCompiler.Export(source.Executable,evidence,Required("--output"));
    }
    else if(args[0]=="import-textures")
    {
        await TexturePackageImporter.ImportAsync(Required("--game-dir"), Required("--output"), Console.WriteLine, rebuild: options.GetValueOrDefault("--rebuild"));
    }
    else if(args[0]=="verify-textures")
    {
        string root = Required("--texture-package");
        var package = await TexturePackageFiles.LoadAsync(root);
        await ImageAssetFiles.VerifyAsync(root, package.Assets);
        Console.WriteLine($"Verified independent texture package: {package.Assets.Count()} logical images.");
    }
    else if(args[0]=="import-client")
    {
        bool messageReports = false;
        if (options.TryGetValue("--message-reports", out var reportOption) && !bool.TryParse(reportOption, out messageReports))
            throw new ArgumentException("--message-reports must be true or false.");
        var content=await ClientContentImporter.ImportAsync(Required("--game-dir"),Required("--scenario"),Required("--texture-package"),Required("--output"),Console.WriteLine, messageReports: messageReports);
        Console.WriteLine($"Complete: {content.Scenario.ScenarioId}, {watch.Elapsed.TotalSeconds:F2}s");
    }
    else if(args[0]=="render-map")
    {
        string root = Required("--content-package");
        var content = await ClientContentFiles.LoadAsync(root);
        var map = content.Map;
        var buildings = BuildingVisuals.Resolve(map,content.Scenario.Settlements.Select(s =>
        {
            var force=s.OwnerId is int owner?content.Scenario.Forces.Single(f=>f.Id==owner):null;
            return new BuildingVisualState(s.Id,s.Durability,s.Appearance,Math.Max(s.Commerce,s.Cultivation),s.Soldiers,force?.ColorCode??50,force?.FlagGlyph??-1);
        }).ToArray()).Buildings;
        string season=options.GetValueOrDefault("--season",map.InitialSeason);
        var common=ImageAssetFiles.Load(content.TextureRoot,map.Assets);
        var land=ImageAssetFiles.Load(content.TextureRoot,map.Seasons.Single(s=>s.Id==season).Textures).ToDictionary(k=>int.Parse(k.Key),v=>v.Value);
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
