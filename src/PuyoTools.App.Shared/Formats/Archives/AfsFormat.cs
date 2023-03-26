﻿using PuyoTools.Archives;
using PuyoTools.Archives.Formats.Afs;
using PuyoTools.Core;
using PuyoTools.Core.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PuyoTools.App.Formats.Archives
{
    /// <inheritdoc/>
    internal partial class AfsFormat : IArchiveFormat
    {
        private AfsFormat() { }

        /// <summary>
        /// Gets the current instance.
        /// </summary>
        internal static AfsFormat Instance { get; } = new AfsFormat();

        public string Name => "AFS";

        public string FileExtension => ".afs";

        public ArchiveBase GetCodec() => new AfsArchive();

        public ArchiveReader CreateReader(Stream source) => new AfsReader(source);

        public bool Identify(Stream source, string filename) => AfsReader.IsFormat(source);
    }
}
