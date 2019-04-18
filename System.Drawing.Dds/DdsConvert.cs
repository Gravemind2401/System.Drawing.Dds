﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace System.Drawing.Dds
{
    public partial class DdsImage
    {
        /// <summary>
        /// Decompresses any compressed pixel data and saves the image to a file on disk using a standard image format.
        /// </summary>
        /// <param name="fileName">The full path of the file to write.</param>
        /// <param name="format">The image format to write with.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToDisk(string fileName, ImageFormat format)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            if (format == null)
                throw new ArgumentNullException(nameof(format));

            var dir = Directory.GetParent(fileName).FullName;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                WriteToStream(fs, format);
        }

        /// <summary>
        /// Decompresses any compressed pixel data and writes the image to a stream using a standard image format.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="format">The image format to write with.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToStream(Stream stream, ImageFormat format)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (format == null)
                throw new ArgumentNullException(nameof(format));

            BitmapEncoder encoder;
            if (format.Equals(ImageFormat.Bmp))
                encoder = new BmpBitmapEncoder();
            else if (format.Equals(ImageFormat.Gif))
                encoder = new GifBitmapEncoder();
            else if (format.Equals(ImageFormat.Jpeg))
                encoder = new JpegBitmapEncoder();
            else if (format.Equals(ImageFormat.Png))
                encoder = new PngBitmapEncoder();
            else if (format.Equals(ImageFormat.Tiff))
                encoder = new TiffBitmapEncoder();
            else throw new NotSupportedException("The specified format is not supported.");

            var source = ToBitmapSource();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
        }

        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>
        /// </summary>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource() => ToBitmapSource(true);

        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>
        /// </summary>
        /// <param name="alpha">Determines whether the alpha channel will be included in the output.</param>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource(bool alpha)
        {
            const double dpi = 96;

            byte[] bgra;
            switch (dx10Header.DxgiFormat)
            {
                case DxgiFormat.BC1_UNorm:
                    bgra = DecompressBC1(data, Height, Width, alpha);
                    break;
                case DxgiFormat.BC2_UNorm:
                    bgra = DecompressBC2(data, Height, Width, alpha);
                    break;
                case DxgiFormat.BC3_UNorm:
                    bgra = DecompressBC3(data, Height, Width, alpha);
                    break;

                default: throw new NotSupportedException("The DxgiFormat is not supported.");
            }

            return BitmapSource.Create(Width, Height, dpi, dpi, alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, bgra, Width * (alpha ? 4 : 3));
        }

        /* https://docs.microsoft.com/en-us/windows/desktop/direct3d10/d3d10-graphics-programming-guide-resources-block-compression */
        internal static byte[] DecompressBC1(byte[] data, int height, int width, bool alpha)
        {
            var output = new BgraColour[width * height];
            var palette = new BgraColour[4];

            var bytesPerBlock = 8;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (int)(yBlock * xBlocks + xBlock) * bytesPerBlock;
                    var c0 = BitConverter.ToUInt16(data, srcIndex);
                    var c1 = BitConverter.ToUInt16(data, srcIndex + 2);

                    palette[0] = BgraColour.From565(c0);
                    palette[1] = BgraColour.From565(c1);

                    if (c0 <= c1)
                    {
                        palette[2] = Lerp(palette[0], palette[1], 1 / 2f);
                        palette[3] = new BgraColour(); //zero on all channels
                    }
                    else
                    {
                        palette[2] = Lerp(palette[0], palette[1], 1 / 3f);
                        palette[3] = Lerp(palette[0], palette[1], 2 / 3f);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        var indexBits = data[srcIndex + 4 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = destY * width + destX;
                            var pIndex = (byte)((indexBits >> j * 2) & 0x3);
                            output[destIndex] = palette[pIndex];
                        }
                    }
                }
            }

            return output.SelectMany(c => c.AsEnumerable(alpha)).ToArray();
        }

        internal static byte[] DecompressBC2(byte[] data, int height, int width, bool alpha)
        {
            var output = new BgraColour[width * height];
            var palette = new BgraColour[4];

            var bytesPerBlock = 16;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (int)(yBlock * xBlocks + xBlock) * bytesPerBlock;
                    palette[0] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 8));
                    palette[1] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 10));

                    palette[2] = Lerp(palette[0], palette[1], 1 / 3f);
                    palette[3] = Lerp(palette[0], palette[1], 2 / 3f);

                    for (int i = 0; i < 4; i++)
                    {
                        var alphaBits = BitConverter.ToUInt16(data, srcIndex + i * 2);
                        var indexBits = data[srcIndex + 12 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = destY * width + destX;
                            var pIndex = (byte)((indexBits >> j * 2) & 0x3);

                            var result = palette[pIndex];
                            result.a = (byte)(((alphaBits >> j * 4) & 0xF) * (0xFF / 0xF));
                            output[destIndex] = result;
                        }
                    }
                }
            }

            return output.SelectMany(c => c.AsEnumerable(alpha)).ToArray();
        }

        internal static byte[] DecompressBC3(byte[] data, int height, int width, bool alpha)
        {
            var output = new BgraColour[width * height];
            var rgbPalette = new BgraColour[4];
            var alphaPalette = new byte[8];

            var bytesPerBlock = 16;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (int)(yBlock * xBlocks + xBlock) * bytesPerBlock;
                    rgbPalette[0] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 8));
                    rgbPalette[1] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 10));

                    rgbPalette[2] = Lerp(rgbPalette[0], rgbPalette[1], 1 / 3f);
                    rgbPalette[3] = Lerp(rgbPalette[0], rgbPalette[1], 2 / 3f);

                    alphaPalette[0] = data[srcIndex];
                    alphaPalette[1] = data[srcIndex + 1];

                    var gradients = alphaPalette[0] > alphaPalette[1] ? 7f : 5f;
                    for (int i = 1; i < gradients; i++)
                        alphaPalette[i + 1] = Lerp(alphaPalette[0], alphaPalette[1], i / gradients);

                    if (alphaPalette[0] <= alphaPalette[1])
                    {
                        alphaPalette[6] = byte.MinValue;
                        alphaPalette[7] = byte.MaxValue;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        var rgbIndexBits = data[srcIndex + 12 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var pixelIndex = i * 4 + j;
                            var alphaStart = srcIndex + (pixelIndex < 8 ? 2 : 5);
                            var alphaIndexBits = (data[alphaStart + 2] << 16) | (data[alphaStart + 1] << 8) | data[alphaStart];

                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = destY * width + destX;
                            var pIndex = (byte)((rgbIndexBits >> j * 2) & 0x3);

                            var result = rgbPalette[pIndex];
                            result.a = alphaPalette[(alphaIndexBits >> (pixelIndex % 8) * 3) & 0x7];
                            output[destIndex] = result;
                        }
                    }
                }
            }

            return output.SelectMany(c => c.AsEnumerable(alpha)).ToArray();
        }

        private static byte Lerp(byte p1, byte p2, float fraction)
        {
            return (byte)((p1 * (1 - fraction)) + (p2 * fraction));
        }

        private static BgraColour Lerp(BgraColour c0, BgraColour c1, float fraction)
        {
            return new BgraColour
            {
                b = Lerp(c0.b, c1.b, fraction),
                g = Lerp(c0.g, c1.g, fraction),
                r = Lerp(c0.r, c1.r, fraction),
                a = Lerp(c0.a, c1.a, fraction)
            };
        }
    }

    internal struct BgraColour
    {
        public byte b, g, r, a;

        public IEnumerable<byte> AsEnumerable(bool alpha)
        {
            yield return b;
            yield return g;
            yield return r;
            if (alpha) yield return a;
        }

        public static BgraColour From565(ushort value)
        {
            byte BMask = 0x1F;
            byte GMask = 0x3F;
            byte RMask = 0x1F;

            return new BgraColour
            {
                b = (byte)((0xFF / BMask) * (value & BMask)),
                g = (byte)((0xFF / GMask) * ((value >> 5) & GMask)),
                r = (byte)((0xFF / RMask) * ((value >> 11) & RMask)),
                a = byte.MaxValue
            };
        }
    }
}