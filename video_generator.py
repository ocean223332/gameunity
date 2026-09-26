#!/usr/bin/env python3
"""
Video Generator for Tiền Tuyến - 20 second promotional video
All visuals generated procedurally with code
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os
import sys

# Video configuration
WIDTH = 1920
HEIGHT = 1080
FPS = 30
DURATION = 20  # seconds
TOTAL_FRAMES = FPS * DURATION

# Colors (Vietnamese flag inspired)
RED = (218, 37, 29)
YELLOW = (255, 204, 0)
BLACK = (0, 0, 0)
WHITE = (255, 255, 255)
DARK_GREEN = (20, 40, 20)
BLOOD_RED = (139, 0, 0)

def ease_in_out(t):
    """Smooth easing function"""
    return t * t * (3 - 2 * t)

def ease_out_bounce(t):
    """Bouncy easing"""
    if t < 1/2.75:
        return 7.5625 * t * t
    elif t < 2/2.75:
        t -= 1.5/2.75
        return 7.5625 * t * t + 0.75
    elif t < 2.5/2.75:
        t -= 2.25/2.75
        return 7.5625 * t * t + 0.9375
    else:
        t -= 2.625/2.75
        return 7.5625 * t * t + 0.984375

def create_noise_texture(width, height, intensity=0.3):
    """Generate film grain noise"""
    noise = np.random.randint(0, int(255 * intensity), (height, width, 3), dtype=np.uint8)
    return Image.fromarray(noise, mode='RGB')

def create_vignette(width, height, strength=0.7):
    """Create vignette overlay - optimized with numpy"""
    center_x, center_y = width // 2, height // 2
    max_radius = np.sqrt(center_x**2 + center_y**2)

    # Use numpy for fast calculation
    y_coords, x_coords = np.ogrid[:height, :width]
    dist = np.sqrt((x_coords - center_x)**2 + (y_coords - center_y)**2)
    fade = 1 - (dist / max_radius) ** 2 * strength
    fade = np.clip(fade * 255, 0, 255).astype(np.uint8)

    return Image.fromarray(fade, mode='L')

def draw_text_with_shadow(draw, text, pos, font, fill_color, shadow_color, shadow_offset=3):
    """Draw text with drop shadow"""
    x, y = pos
    # Shadow
    draw.text((x + shadow_offset, y + shadow_offset), text, font=font, fill=shadow_color)
    # Main text
    draw.text((x, y), text, font=font, fill=fill_color)

def create_title_frame(frame_num, total_frames):
    """Scene 1: Title reveal (0-3s, frames 0-90)"""
    img = Image.new('RGB', (WIDTH, HEIGHT), DARK_GREEN)
    draw = ImageDraw.Draw(img)

    # Progress through scene
    progress = frame_num / total_frames

    # Title animation
    if progress < 0.5:
        # Fade in with scale
        alpha = ease_in_out(progress * 2)

        # Main title
        try:
            title_font = ImageFont.truetype("arial.ttf", 120)
            subtitle_font = ImageFont.truetype("arial.ttf", 40)
        except:
            title_font = ImageFont.load_default()
            subtitle_font = ImageFont.load_default()

        title = "TIỀN TUYẾN"

        # Calculate center position
        bbox = draw.textbbox((0, 0), title, font=title_font)
        text_width = bbox[2] - bbox[0]
        text_height = bbox[3] - bbox[1]

        x = (WIDTH - text_width) // 2
        y = HEIGHT // 2 - 100

        # Scale effect
        scale = 0.5 + 0.5 * alpha

        # Create temporary image for scaling
        temp_img = Image.new('RGBA', (WIDTH, HEIGHT), (0, 0, 0, 0))
        temp_draw = ImageDraw.Draw(temp_img)

        draw_text_with_shadow(temp_draw, title, (x, y), title_font,
                            (*RED, int(255 * alpha)), (*BLACK, int(255 * alpha)))

        # Subtitle
        subtitle = "Front Line"
        bbox_sub = draw.textbbox((0, 0), subtitle, font=subtitle_font)
        sub_width = bbox_sub[2] - bbox_sub[0]
        draw_text_with_shadow(temp_draw, subtitle,
                            ((WIDTH - sub_width) // 2, y + 150),
                            subtitle_font, (*YELLOW, int(255 * alpha)), (*BLACK, int(255 * alpha)))

        # Scale
        if scale != 1.0:
            new_size = (int(WIDTH * scale), int(HEIGHT * scale))
            temp_img = temp_img.resize(new_size, Image.Resampling.LANCZOS)
            offset_x = (WIDTH - new_size[0]) // 2
            offset_y = (HEIGHT - new_size[1]) // 2
            img.paste(temp_img, (offset_x, offset_y), temp_img)
        else:
            img.paste(temp_img, (0, 0), temp_img)
    else:
        # Hold title
        try:
            title_font = ImageFont.truetype("arial.ttf", 120)
            subtitle_font = ImageFont.truetype("arial.ttf", 40)
        except:
            title_font = ImageFont.load_default()
            subtitle_font = ImageFont.load_default()

        title = "TIỀN TUYẾN"
        bbox = draw.textbbox((0, 0), title, font=title_font)
        text_width = bbox[2] - bbox[0]
        x = (WIDTH - text_width) // 2
        y = HEIGHT // 2 - 100

        draw_text_with_shadow(draw, title, (x, y), title_font, RED, BLACK)

        subtitle = "Front Line"
        bbox_sub = draw.textbbox((0, 0), subtitle, font=subtitle_font)
        sub_width = bbox_sub[2] - bbox_sub[0]
        draw_text_with_shadow(draw, subtitle, ((WIDTH - sub_width) // 2, y + 150),
                            subtitle_font, YELLOW, BLACK)

    # Add noise
    noise = create_noise_texture(WIDTH, HEIGHT, 0.1)
    img = Image.blend(img, noise, 0.05)

    return img

def create_gameplay_frame(frame_num, total_frames, weapon_icons):
    """Scene 2: Gameplay showcase (3-13s, frames 90-390)"""
    img = Image.new('RGB', (WIDTH, HEIGHT), (30, 50, 30))
    draw = ImageDraw.Draw(img)

    progress = frame_num / total_frames

    # Animated background grid (simulating arena)
    for i in range(0, WIDTH, 100):
        offset = int((progress * 1000) % 100)
        draw.line([(i + offset, 0), (i + offset, HEIGHT)], fill=(40, 60, 40), width=1)
    for i in range(0, HEIGHT, 100):
        offset = int((progress * 1000) % 100)
        draw.line([(0, i + offset), (WIDTH, i + offset)], fill=(40, 60, 40), width=1)

    # Player circle (center)
    player_x, player_y = WIDTH // 2, HEIGHT // 2
    draw.ellipse([player_x - 30, player_y - 30, player_x + 30, player_y + 30],
                 fill=YELLOW, outline=RED, width=3)

    # Enemies (animated positions)
    enemy_positions = [
        (WIDTH // 4, HEIGHT // 3),
        (3 * WIDTH // 4, HEIGHT // 3),
        (WIDTH // 4, 2 * HEIGHT // 3),
        (3 * WIDTH // 4, 2 * HEIGHT // 3),
    ]

    for i, (ex, ey) in enumerate(enemy_positions):
        # Animate enemy movement
        angle = progress * np.pi * 2 + i * np.pi / 2
        offset_x = int(np.cos(angle) * 50)
        offset_y = int(np.sin(angle) * 50)

        enemy_x = ex + offset_x
        enemy_y = ey + offset_y

        # Enemy shape (triangle pointing toward player)
        angle_to_player = np.arctan2(player_y - enemy_y, player_x - enemy_x)
        size = 25
        points = []
        for j in range(3):
            point_angle = angle_to_player + j * 2 * np.pi / 3
            px = enemy_x + int(np.cos(point_angle) * size)
            py = enemy_y + int(np.sin(point_angle) * size)
            points.append((px, py))

        draw.polygon(points, fill=BLOOD_RED, outline=BLACK)

        # Draw projectile line
        if (frame_num + i * 5) % 20 < 10:
            draw.line([enemy_x, enemy_y, player_x, player_y],
                     fill=(255, 100, 0), width=2)

    # HUD overlay
    try:
        hud_font = ImageFont.truetype("arial.ttf", 32)
    except:
        hud_font = ImageFont.load_default()

    # Top bar info
    wave_num = 1 + int(progress * 6)
    draw.rectangle([20, 20, WIDTH - 20, 80], fill=(0, 0, 0, 180), outline=RED, width=2)
    draw.text((40, 35), f"WAVE: {wave_num}/6", font=hud_font, fill=WHITE)
    draw.text((WIDTH // 2 - 100, 35), "TRƯỜNG SƠN · 1972", font=hud_font, fill=YELLOW)

    # Weapon icons at bottom
    icon_y = HEIGHT - 120
    for i, icon in enumerate(weapon_icons):
        icon_x = WIDTH // 2 - 150 + i * 100
        # Resize icon
        icon_resized = icon.resize((80, 80), Image.Resampling.LANCZOS)
        img.paste(icon_resized, (icon_x, icon_y), icon_resized if icon_resized.mode == 'RGBA' else None)

    # Add vignette
    vignette = create_vignette(WIDTH, HEIGHT, 0.5)
    img = Image.composite(img, Image.new('RGB', (WIDTH, HEIGHT), BLACK), vignette)

    # Film grain
    noise = create_noise_texture(WIDTH, HEIGHT, 0.15)
    img = Image.blend(img, noise, 0.08)

    return img

def create_features_frame(frame_num, total_frames):
    """Scene 3: Features highlight (13-17s, frames 390-510)"""
    img = Image.new('RGB', (WIDTH, HEIGHT), (20, 20, 40))
    draw = ImageDraw.Draw(img)

    progress = frame_num / total_frames

    try:
        title_font = ImageFont.truetype("arial.ttf", 72)
        feature_font = ImageFont.truetype("arial.ttf", 48)
    except:
        title_font = ImageFont.load_default()
        feature_font = ImageFont.load_default()

    features = [
        "SINH TỒN",
        "NÂNG CẤP",
        "CHIẾN ĐẤU"
    ]

    # Staggered feature reveals
    for i, feature in enumerate(features):
        feature_progress = (progress - i * 0.15) / 0.25
        if feature_progress > 0:
            alpha = min(1.0, ease_out_bounce(min(1.0, feature_progress)))

            y_pos = 200 + i * 200
            x_pos = WIDTH // 2 - 200

            # Animated bars
            bar_width = int(400 * alpha)
            draw.rectangle([x_pos, y_pos, x_pos + bar_width, y_pos + 80],
                          fill=RED, outline=YELLOW, width=3)

            # Text
            if alpha > 0.3:
                text_alpha = int((alpha - 0.3) / 0.7 * 255)
                temp_img = Image.new('RGBA', (WIDTH, HEIGHT), (0, 0, 0, 0))
                temp_draw = ImageDraw.Draw(temp_img)

                bbox = temp_draw.textbbox((0, 0), feature, font=feature_font)
                text_width = bbox[2] - bbox[0]
                text_x = x_pos + (400 - text_width) // 2

                draw_text_with_shadow(temp_draw, feature, (text_x, y_pos + 15),
                                    feature_font, (*WHITE, text_alpha), (*BLACK, text_alpha))

                img.paste(temp_img, (0, 0), temp_img)

    return img

def create_ending_frame(frame_num, total_frames):
    """Scene 4: Coming soon (17-20s, frames 510-600)"""
    img = Image.new('RGB', (WIDTH, HEIGHT), BLACK)
    draw = ImageDraw.Draw(img)

    progress = frame_num / total_frames
    alpha = ease_in_out(min(1.0, progress))

    try:
        title_font = ImageFont.truetype("arial.ttf", 100)
        sub_font = ImageFont.truetype("arial.ttf", 50)
    except:
        title_font = ImageFont.load_default()
        sub_font = ImageFont.load_default()

    # Logo
    title = "TIỀN TUYẾN"
    bbox = draw.textbbox((0, 0), title, font=title_font)
    text_width = bbox[2] - bbox[0]
    x = (WIDTH - text_width) // 2

    draw_text_with_shadow(draw, title, (x, HEIGHT // 2 - 100),
                         title_font, RED, BLACK, 5)

    # Coming soon with pulse
    pulse = 0.8 + 0.2 * np.sin(progress * np.pi * 4)
    coming_soon = "COMING SOON"
    bbox_cs = draw.textbbox((0, 0), coming_soon, font=sub_font)
    cs_width = bbox_cs[2] - bbox_cs[0]

    draw_text_with_shadow(draw, coming_soon,
                         ((WIDTH - cs_width) // 2, HEIGHT // 2 + 50),
                         sub_font, tuple([int(c * pulse) for c in YELLOW]), BLACK, 4)

    return img

def load_weapon_icons():
    """Load weapon icons from game assets"""
    icons = []
    icon_names = ['Rifle', 'SMG', 'Shotgun']
    base_path = 'TienTuyen/Assets/_TienTuyen/Art/Resources/WeaponIcons'

    for name in icon_names:
        icon_path = os.path.join(base_path, f'{name}.png')
        if os.path.exists(icon_path):
            try:
                icon = Image.open(icon_path).convert('RGBA')
                icons.append(icon)
            except:
                # Create fallback icon
                icon = Image.new('RGBA', (64, 64), (100, 100, 100, 255))
                icons.append(icon)
        else:
            # Create fallback
            icon = Image.new('RGBA', (64, 64), (100, 100, 100, 255))
            icons.append(icon)

    return icons

# Cache weapon icons globally
_weapon_icons_cache = None

def generate_frame(frame_num):
    """Generate a single frame based on timeline"""
    global _weapon_icons_cache

    # Load weapon icons once
    if _weapon_icons_cache is None:
        _weapon_icons_cache = load_weapon_icons()

    # Timeline: 0-3s title, 3-13s gameplay, 13-17s features, 17-20s ending
    if frame_num < 90:  # 0-3s
        progress = frame_num / 90
        return create_title_frame(frame_num, 90)
    elif frame_num < 390:  # 3-13s
        progress = (frame_num - 90) / 300
        return create_gameplay_frame(frame_num - 90, 300, _weapon_icons_cache)
    elif frame_num < 510:  # 13-17s
        progress = (frame_num - 390) / 120
        return create_features_frame(frame_num - 390, 120)
    else:  # 17-20s
        progress = (frame_num - 510) / 90
        return create_ending_frame(frame_num - 510, 90)

def main():
    """Main video generation pipeline"""
    print(f"Generating {TOTAL_FRAMES} frames at {WIDTH}x{HEIGHT} @ {FPS}fps...")

    # Create output directory
    output_dir = 'video_output/frames'
    os.makedirs(output_dir, exist_ok=True)

    # Pre-load weapon icons
    print("Loading game assets...")
    global _weapon_icons_cache
    _weapon_icons_cache = load_weapon_icons()
    print("✓ Assets loaded")

    # Generate all frames
    import time
    start_time = time.time()

    for frame_num in range(TOTAL_FRAMES):
        frame = generate_frame(frame_num)
        frame.save(f'{output_dir}/frame_{frame_num:04d}.png', optimize=False)

        if (frame_num + 1) % 30 == 0:
            elapsed = time.time() - start_time
            fps = (frame_num + 1) / elapsed
            remaining = (TOTAL_FRAMES - frame_num - 1) / fps if fps > 0 else 0
            print(f"Frame {frame_num + 1}/{TOTAL_FRAMES} | {fps:.1f} fps | ETA: {remaining:.0f}s")

    total_time = time.time() - start_time
    print(f"\n✓ All frames generated in {total_time:.1f}s!")
    print(f"Average: {TOTAL_FRAMES/total_time:.1f} fps")
    print(f"Frames saved to: {output_dir}/")

if __name__ == '__main__':
    main()
