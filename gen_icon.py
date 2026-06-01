from PIL import Image, ImageDraw, ImageFont

def generate_icon(path):
    # Create a 256x256 image with transparent background
    size = (256, 256)
    img = Image.new('RGBA', size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Draw rounded rectangle (modern blue app icon)
    # Pillow doesn't have a simple anti-aliased rounded_rectangle with a wide margin, but we can draw one:
    # A vibrant blue-to-cyan gradient
    draw.rounded_rectangle([12, 12, 244, 244], radius=50, fill=(37, 99, 235, 255))
    
    # Inner stroke (light blue)
    draw.rounded_rectangle([16, 16, 240, 240], radius=46, outline=(96, 165, 250, 255), width=4)

    # Draw a stylized Wi-Fi / Campus Network logo inside
    # Center is at 128, 128
    # Outer wave
    draw.arc([60, 60, 196, 196], 210, 330, fill=(255, 255, 255, 255), width=16)
    # Middle wave
    draw.arc([92, 92, 164, 164], 210, 330, fill=(255, 255, 255, 255), width=16)
    # Inner dot
    draw.ellipse([116, 172, 140, 196], fill=(255, 255, 255, 255))

    # Save as an ICO file containing multiple sizes
    img.save(path, format='ICO', sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])

if __name__ == "__main__":
    import sys
    generate_icon(sys.argv[1])
