﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;

using PuyoTools.Modules;
using PuyoTools.Modules.Compression;
using PuyoTools.Modules.Texture;
using PuyoTools.Formats.Compression;
using PuyoTools.Formats.Textures;
using System.Linq;

namespace PuyoTools.GUI
{
    public partial class TextureEncoder : ToolForm
    {
        Dictionary<ITextureFormat, ModuleSettingsControl> writerSettingsControlsCache;

        public TextureEncoder()
        {
            InitializeComponent();

            // Set up the writer settings panel and format writer settings cache
            writerSettingsControlsCache = new Dictionary<ITextureFormat, ModuleSettingsControl>();

            // Fill the texture format box
            textureFormatBox.SelectedIndex = 0;
            textureFormatBox.Items.AddRange(Texture.EncoderFormats.ToArray());
            textureFormatBox.DisplayMember = nameof(ITextureFormat.Name);

            // Fill the compression format box
            compressionFormatBox.SelectedIndex = 0;
            compressionFormatBox.Items.AddRange(Compression.EncoderFormats.ToArray());
            compressionFormatBox.DisplayMember = nameof(ICompressionFormat.Name);
        }

        private void EnableRunButton()
        {
            runButton.Enabled = (fileList.Count > 0 && textureFormatBox.SelectedIndex > 0);
        }

        private void Run(Settings settings, ProgressDialog dialog)
        {
            for (int i = 0; i < fileList.Count; i++)
            {
                string file = fileList[i];

                // Report progress. If we only have one file to process, no need to display (x of n).
                if (fileList.Count == 1)
                {
                    dialog.ReportProgress(i * 100 / fileList.Count, String.Format("Processing {0}", Path.GetFileName(file)));
                }
                else
                {
                    dialog.ReportProgress(i * 100 / fileList.Count, String.Format("Processing {0} ({1:N0} of {2:N0})", Path.GetFileName(file), i + 1, fileList.Count));
                }

                // Let's open the file.
                // But, we're going to do this in a try catch in case any errors happen.
                try
                {
                    // Set the output path and filename
                    string outPath;
                    if (settings.OutputToSourceDirectory)
                    {
                        outPath = Path.GetDirectoryName(file);
                    }
                    else
                    {
                        outPath = Path.Combine(Path.GetDirectoryName(file), "Encoded Textures");
                    }
                    string outFname = Path.ChangeExtension(Path.GetFileName(file), settings.TextureFormat.FileExtension);

                    MemoryStream buffer = new MemoryStream();

                    // Run it through the texture encoder.
                    TextureBase texture = settings.TextureFormat.GetCodec();

                    using (FileStream source = File.OpenRead(file))
                    {
                        // Set the source path (really only used for GIM textures at the current moment).
                        texture.SourcePath = file;

                        // Set texture settings
                        ModuleSettingsControl settingsControl = settings.WriterSettingsControl;
                        if (settingsControl != null)
                        {
                            Action moduleSettingsAction = () => settingsControl.SetModuleSettings(texture);
                            settingsControl.Invoke(moduleSettingsAction);
                        }

                        texture.Write(source, buffer, (int)source.Length);
                    }

                    // Do we want to compress this texture?
                    if (settings.CompressionFormat != null)
                    {
                        MemoryStream tempBuffer = new MemoryStream();
                        buffer.Position = 0;

                        settings.CompressionFormat.GetCodec().Compress(buffer, tempBuffer);

                        buffer = tempBuffer;
                    }

                    // Create the output path it if it does not exist.
                    if (!Directory.Exists(outPath))
                    {
                        Directory.CreateDirectory(outPath);
                    }

                    // Time to write out the file
                    using (FileStream destination = File.Create(Path.Combine(outPath, outFname)))
                    {
                        buffer.WriteTo(destination);
                    }

                    // Write out the palette file (if one was created along with the texture).
                    if (texture.PaletteStream != null)
                    {
                        using (FileStream destination = File.Create(Path.Combine(outPath, Path.ChangeExtension(outFname, settings.TextureFormat.PaletteFileExtension))))
                        {
                            texture.PaletteStream.Position = 0;
                            texture.PaletteStream.CopyTo(destination);
                        }
                    }

                    // Delete the source if the user chose to
                    if (settings.DeleteSource)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Meh, just ignore the error.
                }
            }
        }

        private void textureFormatBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            textureSettingsPanel.Controls.Clear();

            if (textureFormatBox.SelectedIndex != 0)
            {
                var textureFormat = (ITextureFormat)textureFormatBox.SelectedItem;
                if (!writerSettingsControlsCache.TryGetValue(textureFormat, out var writerSettingsControl))
                {
                    writerSettingsControl = textureFormat.GetModuleSettingsControl();
                    writerSettingsControlsCache.Add(textureFormat, writerSettingsControl);
                }

                textureSettingsPanel.Controls.Add(writerSettingsControl);
            }

            EnableRunButton();
        }

        private void runButton_Click(object sender, EventArgs e)
        {
            // Disable the form
            Enabled = false;

            // Get the format of the texture the user wants to create
            ITextureFormat textureFormat = (ITextureFormat)textureFormatBox.SelectedItem;

            // Set the settings for the tool
            Settings settings = new Settings
            {
                TextureFormat = textureFormat,
                OutputToSourceDirectory = outputToSourceDirButton.Checked,
                DeleteSource = deleteSourceButton.Checked,
                CompressionFormat = compressionFormatBox.SelectedIndex != 0
                    ? (ICompressionFormat)compressionFormatBox.SelectedItem
                    : null,
                WriterSettingsControl = writerSettingsControlsCache.TryGetValue(textureFormat, out var writerSettingsControl)
                    ? writerSettingsControl
                    : null,
            };

            // Set up the process dialog and then run the tool
            ProgressDialog dialog = new ProgressDialog
            {
                WindowTitle = "Processing",
                Title = "Encoding Textures",
            };
            dialog.DoWork += (sender2, e2) => Run(settings, dialog);
            dialog.RunWorkerCompleted += (sender2, e2) => Close();
            dialog.RunWorkerAsync();
        }

        private struct Settings
        {
            public ITextureFormat TextureFormat;
            public ICompressionFormat CompressionFormat;
            public ModuleSettingsControl WriterSettingsControl;

            public bool OutputToSourceDirectory;
            public bool DeleteSource;
        }
    }
}
