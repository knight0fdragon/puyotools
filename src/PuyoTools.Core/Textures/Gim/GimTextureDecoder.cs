﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PuyoTools.Core.Textures.Gim
{
    public class GimTextureDecoder
    {
        #region Fields
        private GimPixelCodec paletteCodec; // Palette codec
        private GimDataCodec pixelCodec;    // Pixel codec

        private ushort paletteEntries; // Number of entries in the palette

        private bool isSwizzled; // Is the texture data swizzled?

        private static readonly byte[] magicCode =
        {
            (byte)'M', (byte)'I', (byte)'G', (byte)'.',
            (byte)'0', (byte)'0', (byte)'.',
            (byte)'1', (byte)'P', (byte)'S', (byte)'P',
            0,
        };

        private byte[] paletteData;
        private byte[] textureData;

        private byte[] decodedData;

        private int actualWidth;
        private int actualHeight;
        #endregion

        #region Texture Properties
        /// <summary>
        /// Gets the width.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the palette format, or null if a palette is not used.
        /// </summary>
        public GimPaletteFormat? PaletteFormat { get; private set; }

        /// <summary>
        /// Gets the pixel format.
        /// </summary>
        public GimDataFormat PixelFormat { get; private set; }
        #endregion

        #region Constructors & Initalizers
        /// <summary>
        /// Open a GIM texture from a file.
        /// </summary>
        /// <param name="file">Filename of the file that contains the texture data.</param>
        public GimTextureDecoder(string file)
        {
            using (var stream = File.OpenRead(file))
            {
                Initialize(stream);
            }
        }

        /// <summary>
        /// Open a GIM texture from a stream.
        /// </summary>
        /// <param name="source">Stream that contains the texture data.</param>
        public GimTextureDecoder(Stream source)
        {
            Initialize(source);
        }

        private void Initialize(Stream source)
        {
            // Check to see if what we are dealing with is a GIM texture
            if (!Is(source))
            {
                throw new InvalidFormatException("Not a valid GIM texture.");
            }

            var startPosition = source.Position;
            var reader = new BinaryReader(source);

            paletteEntries = 0;

            // A GIM is constructed of different chunks. They do not necessarily have to be in order.
            int eofOffset = -1;

            source.Position += 0x10;
            while (source.Position < source.Length)
            {
                var chunkPosition = source.Position;
                int chunkLength;

                var chunkType = reader.ReadUInt16();
                source.Position += 2; // 0x04

                switch (chunkType)
                {
                    case 0x02: // EOF offset chunk

                        eofOffset = reader.ReadInt32() + 16;

                        // Get the length of this chunk
                        chunkLength = reader.ReadInt32();

                        break;

                    case 0x03: // Metadata offset chunk

                        // Skip this chunk. It's not necessary for decoding this texture.
                        source.Position += 4; // 0x08
                        chunkLength = reader.ReadInt32();

                        break;

                    case 0x04: // Texture data chunk

                        // Get the length of this chunk
                        source.Position += 4; // 0x08
                        chunkLength = reader.ReadInt32();

                        // Get the pixel format & codec
                        source.Position += 8; // 0x20
                        PixelFormat = (GimDataFormat)reader.ReadUInt16();
                        pixelCodec = GimDataCodec.GetDataCodec(PixelFormat);

                        // Get whether this texture is swizzled
                        isSwizzled = reader.ReadUInt16() == 1;

                        // Get the texture dimensions
                        Width = actualWidth = reader.ReadUInt16();
                        Height = actualHeight = reader.ReadUInt16();

                        // Some textures do not have a width that is a multiple or 16 or a height that is a multiple of 8.
                        // We'll just do it the lazy way and set their width/height to a multiple of 16/8.
                        if (actualWidth % 16 != 0)
                        {
                            actualWidth = PTMethods.RoundUp(actualWidth, 16);
                        }
                        if (actualHeight % 8 != 0)
                        {
                            actualHeight = PTMethods.RoundUp(actualHeight, 8);
                        }

                        // If we don't have a known pixel codec for this format, that's ok.
                        // This will allow the properties to be read if the user doesn't want to decode this texture.
                        // The exception will be thrown when the texture is being decoded.
                        if (pixelCodec is null)
                        {
                            break;
                        }

                        // Read the texture data
                        textureData = reader.At(chunkPosition + 0x50, x => x.ReadBytes(actualWidth * actualHeight * pixelCodec.Bpp / 8));

                        break;

                    case 0x05: // Palette data chunk

                        // Get the length of this chunk
                        source.Position += 4; // 0x08
                        chunkLength = reader.ReadInt32();

                        // Get the palette format & codec
                        source.Position += 8; // 0x20
                        PaletteFormat = (GimPaletteFormat)reader.ReadUInt16();
                        paletteCodec = GimPixelCodec.GetPixelCodec(PaletteFormat.Value);

                        // Get the number of entries in the palette
                        source.Position += 2; // 0x24
                        paletteEntries = reader.ReadUInt16();

                        // If we don't have a known palette codec for this format, that's ok.
                        // This will allow the properties to be read if the user doesn't want to decode this texture.
                        // The exception will be thrown when the texture is being decoded.
                        if (paletteCodec is null)
                        {
                            break;
                        }

                        // Read the palette data
                        paletteData = reader.At(chunkPosition + 0x50, x => x.ReadBytes(paletteEntries * paletteCodec.Bpp / 8));

                        break;

                    case 0xFF: // Metadata chunk

                        // Get the length of this chunk
                        source.Position += 4; // 0x08
                        chunkLength = reader.ReadInt32();

                        // Read the metadata
                        source.Position += 4; // 0x10

                        Metadata = new GimMetadata
                        {
                            OriginalFilename = reader.ReadNullTerminatedString(),
                            User = reader.ReadNullTerminatedString(),
                            Timestamp = reader.ReadNullTerminatedString(),
                            Program = reader.ReadNullTerminatedString()
                        };

                        break;

                    default: // Unknown chunk

                        throw new InvalidFormatException($"Unknown chunk type {chunkType:X}");
                }

                // Verify that the chunk length will allow the stream to progress
                if (chunkLength <= 0)
                {
                    throw new InvalidFormatException("Chunk length cannot be zero or negative.");
                }

                // Go to the next chunk
                source.Position = chunkPosition + chunkLength;
            }

            // Verify that the stream's position is as the expected position
            if (source.Position - startPosition != eofOffset)
            {
                throw new InvalidFormatException("Stream position does not match expected end-of-file position.");
            }

            // If we don't have a known pixel codec for this format, that's ok.
            // This will allow the properties to be read if the user doesn't want to decode this texture.
            // The exception will be thrown when the texture is being decoded.
            if (pixelCodec is null)
            {
                return;
            }

            if (pixelCodec.PaletteEntries != 0)
            {
                // If we don't have a known palette codec for this format, that's ok.
                // This will allow the properties to be read if the user doesn't want to decode this texture.
                // The exception will be thrown when the texture is being decoded.
                if (paletteCodec is null)
                {
                    return;
                }

                // Verify that there aren't too many entries in the palette
                if (paletteEntries > pixelCodec.PaletteEntries)
                {
                    throw new InvalidFormatException("Too many entries in palette for the specified pixel format.");
                }

                // Set the data format's palette codec
                pixelCodec.PixelCodec = paletteCodec;
            }
        }
        #endregion

        #region Metadata
        /// <summary>
        /// Gets the metadata, or null if there is no metadata.
        /// </summary>
        public GimMetadata Metadata { get; private set; }
        #endregion

        #region Texture Retrieval
        /// <summary>
        /// Saves the decoded texture to the specified file as a PNG.
        /// </summary>
        /// <param name="file">Name of the file to save the data to.</param>
        public void Save(string file)
        {
            using (var stream = File.OpenWrite(file))
            {
                Save(stream);
            }
        }

        /// <summary>
        /// Saves the decoded texture to the specified stream as a PNG.
        /// </summary>
        /// <param name="destination">The stream to save the texture to.</param>
        public void Save(Stream destination)
        {
            var image = Image.LoadPixelData<Bgra32>(GetPixelData(), Width, Height);
            image.Save(destination, new PngEncoder());
        }

        // Decodes a texture
        private byte[] DecodeTexture()
        {
            // Verify that a palette codec (if required) and pixel codec have been set.
            if (pixelCodec is null)
            {
                throw new NotSupportedException($"Pixel format {PixelFormat:X} is not supported for decoding.");
            }
            if (paletteCodec is null && pixelCodec.PaletteEntries != 0)
            {
                throw new NotSupportedException($"Palette format {PaletteFormat:X} is not supported for decoding.");
            }

            if (paletteData != null) // The texture contains an embedded palette
            {
                pixelCodec.SetPalette(paletteData, 0, paletteEntries);
            }

            if (isSwizzled)
            {
                return pixelCodec.Decode(GimDataCodec.UnSwizzle(textureData, 0, actualWidth, actualHeight, pixelCodec.Bpp), 0, actualWidth, actualHeight);
            }

            return pixelCodec.Decode(textureData, 0, actualWidth, actualHeight);
        }

        /// <summary>
        /// Decodes the texture and returns the pixel data.
        /// </summary>
        /// <returns>The pixel data as a byte array.</returns>
        public byte[] GetPixelData()
        {
            if (decodedData == null)
            {
                decodedData = DecodeTexture();
            }

            return decodedData;
        }
        #endregion

        #region Texture Check
        /// <summary>
        /// Determines if this is a GIM texture.
        /// </summary>
        /// <param name="source">The stream to read.</param>
        /// <returns>True if this is a GIM texture, false otherwise.</returns>
        public static bool Is(Stream source)
        {
            var startPosition = source.Position;
            var remainingLength = source.Length - startPosition;

            using (var reader = new BinaryReader(source, Encoding.UTF8, true))
            {
                return remainingLength > 24
                    && reader.At(startPosition, x => x.ReadBytes(magicCode.Length)).SequenceEqual(magicCode)
                    && reader.At(startPosition + 0x14, x => x.ReadUInt32()) == remainingLength - 16;
            }
        }

        /// <summary>
        /// Determines if this is a GIM texture.
        /// </summary>
        /// <param name="file">Filename of the file that contains the data.</param>
        /// <returns>True if this is a GIM texture, false otherwise.</returns>
        public static bool Is(string file)
        {
            using (FileStream stream = File.OpenRead(file))
            {
                return Is(stream);
            }
        }
        #endregion
    }
}