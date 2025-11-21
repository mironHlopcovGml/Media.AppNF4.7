using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Media.VideoProcessing
{
    internal static class StreamReaderExtensions
    {
        public static Task<string> ReadToEndAsync(this StreamReader reader, CancellationToken ct)
        {
            return Task.Run(reader.ReadToEnd, ct);
        }
    }
}
