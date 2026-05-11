using System.Drawing;
using System.IO;

var bitmap = new Bitmap("c:\\Users\\ZincG\\Documents\\drcom\\icon.png");
var iconStream = new FileStream("c:\\Users\\ZincG\\Documents\\drcom\\CampusNetworkLogin\\Resources\\icon.ico", FileMode.Create);

// Write ICO header
iconStream.WriteByte(0); iconStream.WriteByte(0);
iconStream.WriteByte(1); iconStream.WriteByte(0);
iconStream.WriteByte(1); iconStream.WriteByte(0);

// Write image info
var width = bitmap.Width; if (width >= 256) width = 0;
var height = bitmap.Height; if (height >= 256) height = 0;
iconStream.WriteByte((byte)width);
iconStream.WriteByte((byte)height);
iconStream.WriteByte(0); // colors
iconStream.WriteByte(0); // reserved
iconStream.WriteByte(1); // panes
iconStream.WriteByte(0); 
iconStream.WriteByte(32); // bpp
iconStream.WriteByte(0);

// We need PNG stream for the actual image data
var pngStream = new MemoryStream();
bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
var pngBytes = pngStream.ToArray();

// size of image data
var size = pngBytes.Length;
iconStream.WriteByte((byte)(size & 0xFF));
iconStream.WriteByte((byte)((size >> 8) & 0xFF));
iconStream.WriteByte((byte)((size >> 16) & 0xFF));
iconStream.WriteByte((byte)((size >> 24) & 0xFF));

// offset to image data
iconStream.WriteByte(22);
iconStream.WriteByte(0);
iconStream.WriteByte(0);
iconStream.WriteByte(0);

iconStream.Write(pngBytes, 0, size);
iconStream.Flush();
iconStream.Close();
