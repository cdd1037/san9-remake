"""Original-style static building information panels for map exports."""

from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

from .canvas import RgbCanvas
from .graphics import parse_palette_image_archive
from .scenario import MAP_WIDTH, BuildingSnapshot, ImageRecord, Square
from .terrain import region_building_base

TRAINING_CITY_CODES = frozenset((0, 3, 8, 14, 17, 20, 25, 36, 42))
EMPEROR_ICON_RESOURCE = 1243
TRAINING_ICON_RESOURCE = 1244
MERCHANT_ICON_RESOURCE = 1245
MARKER_RESOURCE_BASE = 1220
MARKER_PALETTE_INDEX = 3
COMMON_PALETTE_BASE = 200
COMMON_RESOURCE_PALETTES = {
    **{resource_id: 251 - COMMON_PALETTE_BASE for resource_id in range(390, 394)},
    **{resource_id: 252 - COMMON_PALETTE_BASE for resource_id in range(394, 398)},
}
MORALE_BAR_RESOURCES = (390, 392, 395, 394)
DURABILITY_BAR_RESOURCES = (391, 393, 397, 396)
SOLDIER_TEXT_X = 16
SOLDIER_TEXT_WIDTH = 56
# Fixed-building code -> information-panel offset from the building visual base.
# The original indexes the 17-entry table at 0x660948 through 0x522D60.
BUILDING_INFO_OFFSETS = (
    (12, -128),
    (12, -74),
    (28, -68),
    (6, -66),
    (80, -106),
    (58, -110),
    (36, -64),
    (48, -56),
    (40, -70),
    (16, -78),
    (25, -74),
    (-63, -81),
    (16, -80),
    (-76, -78),
    (10, -68),
    (-63, -72),
    (-2, -97),
)
# The executable appends these static type labels after the serialized base name.
# The working semantic catalog is simplified Chinese, so use its matching forms.
BUILDING_NAME_SUFFIXES = {
    1: "港",
    6: "阵",
    7: "砦",
    8: "箭楼",
    9: "城塞",
    10: "栅栏",
    11: "栅栏",
    12: "土砂",
    13: "土砂",
    14: "土垒",
    15: "土垒",
    16: "石兵",
}


@dataclass(frozen=True)
class BuildingInfoResources:
    common_palettes: list[list[tuple[int, int, int]]]
    common_images: list[ImageRecord]
    marker_palettes: list[list[tuple[int, int, int]]]
    marker_images: list[ImageRecord]
    font: object

    @classmethod
    def load(cls, data_dir: Path, font_path: Path) -> BuildingInfoResources:
        try:
            from PIL import ImageFont
        except ImportError as exc:
            raise RuntimeError(
                "--building-info requires Pillow; install research/scripts/requirements.txt"
            ) from exc
        if font_path.suffix.lower() not in (".ttf", ".otf"):
            raise ValueError("--building-info-font must be a TTF or OTF file")
        common_palettes, common_images = parse_palette_image_archive(
            data_dir / "G_Common.s9"
        )
        marker_palettes, marker_images = parse_palette_image_archive(
            data_dir / "G_Marker.s9"
        )
        validate_resource_counts(
            common_palettes, common_images, marker_palettes, marker_images
        )
        return cls(
            common_palettes,
            common_images,
            marker_palettes,
            marker_images,
            ImageFont.truetype(str(font_path), 12),
        )


def validate_resource_counts(
    common_palettes: list[object],
    common_images: list[object],
    marker_palettes: list[object],
    marker_images: list[object],
) -> None:
    if len(common_palettes) != 67 or len(common_images) != 491:
        raise ValueError("G_Common.s9 must contain 67 palettes and 491 images")
    if len(marker_palettes) != 5 or len(marker_images) != 34:
        raise ValueError("G_Marker.s9 must contain 5 palettes and 34 images")


def validate_morale(value: int) -> int:
    if not 0 <= value <= 100:
        raise ValueError("building morale must be between 0 and 100")
    return value


@dataclass(frozen=True)
class BuildingInfoContext:
    resources: BuildingInfoResources
    names: dict[tuple[str, int], str]
    morale: int
    emperor_city_code: int | None


def read_semantic_catalog(path: Path) -> dict[tuple[str, int], str]:
    names: dict[tuple[str, int], str] = {}
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != ["kind", "id", "name"]:
            raise ValueError(f"{path}: expected kind,id,name header")
        for row_number, row in enumerate(reader, 2):
            try:
                key = (row["kind"], int(row["id"]))
            except (TypeError, ValueError) as exc:
                raise ValueError(f"{path}:{row_number}: invalid semantic id") from exc
            if key in names:
                raise ValueError(f"{path}:{row_number}: duplicate semantic key {key}")
            names[key] = row["name"]
    return names


def building_name_key(building: BuildingSnapshot) -> tuple[str, int]:
    return (
        ("city", building.city_code)
        if building.is_city
        else ("zone", building.zone_code - 50)
    )


def resolve_building_name(
    building: BuildingSnapshot,
    names: dict[tuple[str, int], str],
) -> str:
    key = building_name_key(building)
    if key not in names:
        raise ValueError(f"semantic catalog is missing visible building {key}")
    return names[key] + BUILDING_NAME_SUFFIXES.get(building.fixed_building_code, "")


def shared_icon_resource(building: BuildingSnapshot) -> int | None:
    if not building.is_city:
        return None
    if building.permanent_merchant:
        return MERCHANT_ICON_RESOURCE
    if building.city_code in TRAINING_CITY_CODES:
        return TRAINING_ICON_RESOURCE
    return None


def building_hosts_emperor(
    building: BuildingSnapshot, emperor_city_code: int | None
) -> bool:
    return building.is_city and building.city_code == emperor_city_code


def panel_width(building: BuildingSnapshot, emperor_city_code: int | None) -> int:
    if building_hosts_emperor(building, emperor_city_code):
        return 116
    if shared_icon_resource(building) is not None:
        return 95
    return 76


def panel_origin(
    base_x: int,
    base_y: int,
    margin: int,
    fixed_building_code: int,
) -> tuple[int, int]:
    """Apply the original per-building-type information-panel offset."""

    if not 0 <= fixed_building_code < len(BUILDING_INFO_OFFSETS):
        raise ValueError(
            f"building info has invalid fixed-building code {fixed_building_code}"
        )
    offset_x, offset_y = BUILDING_INFO_OFFSETS[fixed_building_code]
    return margin + base_x + offset_x, margin + base_y + offset_y


def displayed_morale(building: BuildingSnapshot, configured_morale: int) -> int:
    return configured_morale if building.soldiers > 0 else 0


def status_bar_width(current: int, maximum: int) -> int:
    if maximum <= 0:
        return 0
    return max(0, min(68, (68 * current + maximum - 68) // maximum))


def status_bar_fill_resource(
    current: int,
    maximum: int,
    resource_ids: tuple[int, int, int, int],
) -> int:
    _, normal_id, warning_id, critical_id = resource_ids
    if current < 20 * maximum // 100:
        return critical_id
    if current < 40 * maximum // 100:
        return warning_id
    return normal_id


def _draw_indexed_prefix(
    canvas: RgbCanvas,
    image: ImageRecord,
    palette: list[tuple[int, int, int]],
    target_x: int,
    target_y: int,
    visible_width: int,
) -> None:
    for image_y in range(image.height):
        for image_x in range(min(image.width, visible_width)):
            pixel_index = image.pixels[image_y * image.width + image_x]
            if pixel_index:
                canvas.put(target_x + image_x, target_y + image_y, palette[pixel_index])


def _draw_status_bar(
    canvas: RgbCanvas,
    resources: BuildingInfoResources,
    x: int,
    y: int,
    current: int,
    maximum: int,
    resource_ids: tuple[int, int, int, int],
) -> None:
    frame_id = resource_ids[0]
    ratio_width = status_bar_width(current, maximum)
    fill_id = status_bar_fill_resource(current, maximum, resource_ids)
    frame = resources.common_images[frame_id]
    frame_palette = resources.common_palettes[COMMON_RESOURCE_PALETTES[frame_id]]
    canvas.draw_indexed(frame, frame_palette, x, y)
    fill = resources.common_images[fill_id]
    fill_palette = resources.common_palettes[COMMON_RESOURCE_PALETTES[fill_id]]
    _draw_indexed_prefix(canvas, fill, fill_palette, x + 4, y + 4, ratio_width)


def _draw_text(
    canvas: RgbCanvas,
    font: object,
    text: str,
    x: int,
    y: int,
    right_edge: int | None = None,
) -> None:
    from PIL import Image, ImageDraw

    image = Image.new("RGBA", (max(1, len(text) * 14 + 4), 18), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.text(
        (1, 0),
        text,
        font=font,
        fill=(255, 255, 255, 255),
        stroke_width=1,
        stroke_fill=(24, 24, 24, 255),
        anchor="lt",
    )
    target_x = x
    if right_edge is not None:
        alpha_bounds = image.getbbox()
        if alpha_bounds is not None:
            target_x = right_edge - alpha_bounds[2]
    for image_y in range(image.height):
        for image_x in range(image.width):
            red, green, blue, alpha = image.getpixel((image_x, image_y))
            if alpha:
                canvas.put(
                    target_x + image_x,
                    y + image_y,
                    (red, green, blue),
                    alpha,
                )


def draw_building_info_panels(
    squares: list[Square],
    buildings: list[BuildingSnapshot],
    context: BuildingInfoContext,
    x0: int,
    y0: int,
    width: int,
    height: int,
    margin: int,
    canvas: RgbCanvas,
) -> int:
    drawn = 0
    for building in sorted(buildings, key=lambda item: (item.x + item.y, item.x)):
        if not (x0 <= building.x < x0 + width and y0 <= building.y < y0 + height):
            continue
        name = resolve_building_name(building, context.names)
        square = squares[building.y * MAP_WIDTH + building.x]
        base_x, base_y = region_building_base(
            building.x - x0, building.y - y0, height, square.offset
        )
        panel_x, panel_y = panel_origin(
            base_x, base_y, margin, building.fixed_building_code
        )
        _draw_text(
            canvas,
            context.resources.font,
            str(building.soldiers),
            panel_x + SOLDIER_TEXT_X,
            panel_y + 4,
            right_edge=panel_x + SOLDIER_TEXT_X + SOLDIER_TEXT_WIDTH,
        )
        _draw_status_bar(
            canvas,
            context.resources,
            panel_x,
            panel_y + 16,
            building.durability,
            building.max_durability,
            DURABILITY_BAR_RESOURCES,
        )
        _draw_status_bar(
            canvas,
            context.resources,
            panel_x,
            panel_y + 29,
            displayed_morale(building, context.morale),
            100,
            MORALE_BAR_RESOURCES,
        )
        _draw_text(
            canvas,
            context.resources.font,
            name,
            panel_x,
            panel_y + 39,
        )
        icon_id = shared_icon_resource(building)
        if icon_id is not None:
            canvas.draw_indexed(
                context.resources.marker_images[icon_id - MARKER_RESOURCE_BASE],
                context.resources.marker_palettes[MARKER_PALETTE_INDEX],
                panel_x + 75,
                panel_y + 17,
            )
        if building_hosts_emperor(building, context.emperor_city_code):
            canvas.draw_indexed(
                context.resources.marker_images[
                    EMPEROR_ICON_RESOURCE - MARKER_RESOURCE_BASE
                ],
                context.resources.marker_palettes[MARKER_PALETTE_INDEX],
                panel_x + 96,
                panel_y + 17,
            )
        drawn += 1
    return drawn
