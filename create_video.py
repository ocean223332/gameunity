#!/usr/bin/env python3
"""
Main video creation pipeline - orchestrates frame generation, audio, and FFmpeg encoding
"""
import subprocess
import sys
import os
import time

def run_command(cmd, description):
    """Run a command and show progress"""
    print(f"\n{'='*60}")
    print(f"⚙️  {description}")
    print(f"{'='*60}")

    start_time = time.time()
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    elapsed = time.time() - start_time

    if result.returncode == 0:
        print(f"✓ Completed in {elapsed:.1f}s")
        if result.stdout:
            print(result.stdout)
        return True
    else:
        print(f"✗ Failed!")
        print(result.stderr)
        return False

def check_dependencies():
    """Check if required tools are available"""
    print("Checking dependencies...")

    # Check Python packages
    try:
        import numpy
        import PIL
        print("✓ Python packages (numpy, PIL) available")
    except ImportError as e:
        print(f"✗ Missing Python package: {e}")
        print("\nInstall with:")
        print("  pip install numpy pillow")
        return False

    # Check FFmpeg
    result = subprocess.run("ffmpeg -version", shell=True, capture_output=True)
    if result.returncode != 0:
        print("✗ FFmpeg not found")
        return False
    print("✓ FFmpeg available")

    return True

def main():
    """Main pipeline"""
    print("""
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║              TIỀN TUYẾN VIDEO GENERATOR                   ║
║          Procedural video creation pipeline               ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
    """)

    # Check dependencies
    if not check_dependencies():
        print("\n✗ Dependency check failed. Please install missing components.")
        sys.exit(1)

    print("\n✓ All dependencies satisfied\n")

    # Step 1: Generate video frames
    if not run_command("python video_generator.py", "Step 1/3: Generating video frames"):
        sys.exit(1)

    # Step 2: Generate audio
    if not run_command("python audio_generator.py", "Step 2/3: Generating audio track"):
        sys.exit(1)

    # Step 3: Combine with FFmpeg
    print(f"\n{'='*60}")
    print("⚙️  Step 3/3: Encoding final video with FFmpeg")
    print(f"{'='*60}")

    # FFmpeg command to combine frames and audio
    ffmpeg_cmd = [
        "ffmpeg",
        "-y",  # Overwrite output
        "-framerate", "30",
        "-i", "video_output/frames/frame_%04d.png",
        "-i", "video_output/audio.wav",
        "-c:v", "libx264",
        "-preset", "medium",
        "-crf", "18",  # High quality
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-b:a", "192k",
        "-shortest",
        "video_output/tien_tuyen_trailer.mp4"
    ]

    start_time = time.time()
    result = subprocess.run(ffmpeg_cmd, capture_output=True, text=True)
    elapsed = time.time() - start_time

    if result.returncode == 0:
        print(f"✓ Video encoded in {elapsed:.1f}s")

        # Get file size
        video_path = "video_output/tien_tuyen_trailer.mp4"
        if os.path.exists(video_path):
            file_size = os.path.getsize(video_path)
            file_size_mb = file_size / (1024 * 1024)

            print(f"""
╔═══════════════════════════════════════════════════════════╗
║                    ✓ SUCCESS!                             ║
╚═══════════════════════════════════════════════════════════╝

📹 Video created: {video_path}
📊 File size: {file_size_mb:.2f} MB
⏱️  Duration: 20 seconds
🎬 Resolution: 1920x1080 @ 30fps
🎵 Audio: AAC 192kbps

You can now play the video!
            """)
    else:
        print("✗ FFmpeg encoding failed!")
        print(result.stderr)
        sys.exit(1)

if __name__ == '__main__':
    main()
