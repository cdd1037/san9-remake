"""RGB canvas operations shared by map-layer renderers."""

from __future__ import annotations

from dataclasses import dataclass, field

from .graphics import palette_alpha_blend
from .scenario import ImageRecord

Color = tuple[int, int, int]
Palette = list[Color]


@dataclass
class RgbCanvas:
    width: int
    height: int
    pixels: bytearray = field(init=False)

    def __post_init__(self) -> None:
        if self.width <= 0 or self.height <= 0:
            raise ValueError("canvas dimensions must be positive")
        self.pixels = bytearray(self.width * self.height * 3)

    def put(self, x: int, y: int, color: Color, weight: int = 255) -> None:
        if not (0 <= x < self.width and 0 <= y < self.height):
            return
        offset = (y * self.width + x) * 3
        if weight >= 255:
            self.pixels[offset : offset + 3] = bytes(color)
        elif weight > 0:
            old = self.pixels[offset : offset + 3]
            self.pixels[offset : offset + 3] = bytes(
                (weight * channel + (255 - weight) * old[index]) // 255
                for index, channel in enumerate(color)
            )

    def put_mask_edge(
        self, x: int, y: int, color: Color, target_weight: int
    ) -> None:
        if not (0 <= x < self.width and 0 <= y < self.height):
            return
        if target_weight >= 256:
            return
        offset = (y * self.width + x) * 3
        old = self.pixels[offset : offset + 3]
        source_weight = 256 - target_weight
        self.pixels[offset : offset + 3] = bytes(
            (source_weight * channel + target_weight * old[index]) >> 8
            for index, channel in enumerate(color)
        )

    def draw_indexed(
        self,
        image: ImageRecord,
        palette: Palette,
        target_x: int,
        target_y: int,
        weight: int = 255,
        palette_alphas: list[int] | None = None,
    ) -> None:
        for image_y in range(image.height):
            for image_x in range(image.width):
                pixel_index = image.pixels[image_y * image.width + image_x]
                if pixel_index == 0:
                    continue
                x = target_x + image_x
                y = target_y + image_y
                if palette_alphas is None:
                    self.put(x, y, palette[pixel_index], weight)
                elif 0 <= x < self.width and 0 <= y < self.height:
                    offset = (y * self.width + x) * 3
                    self.pixels[offset : offset + 3] = palette_alpha_blend(
                        tuple(self.pixels[offset : offset + 3]),
                        palette[pixel_index],
                        palette_alphas[pixel_index],
                    )

    def to_bytes(self) -> bytes:
        return bytes(self.pixels)
