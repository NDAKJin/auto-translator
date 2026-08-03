"""生成 AutoTranslator 模组的 About/Preview.png 图标（512x512，透明背景）。

设计：深色圆角方块底 + 语言气泡 + 翻译箭头符号 + 居中「A」字标识，
采用 RimWorld 创意工坊常见简洁扁平风格。
"""
from PIL import Image, ImageDraw, ImageFont
import os

SIZE = 512
OUT = os.path.join(os.path.dirname(__file__), "..", "Publish", "AutoTranslator", "About", "Preview.png")

BG = (32, 38, 52, 255)          # 深蓝灰底
BG_ROUND = (44, 52, 70, 255)
ACCENT = (88, 196, 221, 255)    # 青色强调（翻译/语言）
TEXT = (235, 240, 248, 255)
SHADOW = (0, 0, 0, 90)


def rounded_rect(draw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)


def main():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 圆角底（留 16px 边距）
    margin = 16
    rounded_rect(draw, [margin, margin, SIZE - margin, SIZE - margin], 96, BG)

    # 语言气泡：左上大圆 + 小尾巴，代表「对话/语言」
    cx, cy = SIZE // 2, int(SIZE * 0.40)
    bubble_r = 120
    rounded_rect(
        draw,
        [cx - bubble_r, cy - bubble_r, cx + bubble_r, cy + bubble_r],
        48,
        ACCENT,
    )
    # 气泡尾巴（小三角）
    tail = [
        (cx - 36, cy + bubble_r - 18),
        (cx - 90, cy + bubble_r + 40),
        (cx - 8, cy + bubble_r + 6),
    ]
    draw.polygon(tail, fill=ACCENT)

    # 气泡内的「翻译」双箭头（左右对指）
    arrow_y = cy
    aw, ah = 64, 26
    gap = 30
    left_tip = cx - gap - aw
    right_tip = cx + gap + aw
    # 左箭头：指向右
    draw.line([(left_tip, arrow_y), (cx - gap, arrow_y)], fill=BG, width=14)
    draw.polygon(
        [(cx - gap + 14, arrow_y - ah), (cx - gap, arrow_y), (cx - gap + 14, arrow_y + ah)],
        fill=BG,
    )
    # 右箭头：指向左
    draw.line([(right_tip, arrow_y), (cx + gap, arrow_y)], fill=BG, width=14)
    draw.polygon(
        [(cx + gap - 14, arrow_y - ah), (cx + gap, arrow_y), (cx + gap - 14, arrow_y + ah)],
        fill=BG,
    )

    # 底部「A」字标识（AutoTranslator）
    font_path = os.path.join(os.path.dirname(__file__), "..", "Publish", "AutoTranslator", "About")
    # 尝试加载系统字体，退化到默认
    font = None
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/seguiemj.ttf",
    ]
    for c in candidates:
        if os.path.exists(c):
            try:
                font = ImageFont.truetype(c, 150)
                break
            except Exception:
                continue
    if font is None:
        font = ImageFont.load_default()

    letter = "A"
    # 阴影
    draw.text((cx + 3, int(SIZE * 0.70) + 3), letter, font=font, fill=SHADOW, anchor="mm")
    draw.text((cx, int(SIZE * 0.70)), letter, font=font, fill=TEXT, anchor="mm")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("saved:", os.path.abspath(OUT), img.size, img.mode)


if __name__ == "__main__":
    main()
