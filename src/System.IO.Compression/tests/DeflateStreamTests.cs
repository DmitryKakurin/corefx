// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace System.IO.Compression.Tests
{
    public class ZLibDeflateStreamTests : DeflateStreamTests, IDisposable
    {
        public ZLibDeflateStreamTests() { Common.SetDeflaterMode("zlib"); }
        public void Dispose() { Common.SetDeflaterMode("unknown"); }
    }

    public class ManagedDeflateStreamTests : DeflateStreamTests, IDisposable
    {
        public ManagedDeflateStreamTests() { Common.SetDeflaterMode("managed"); }
        public void Dispose() { Common.SetDeflaterMode("unknown"); }
    }

    public abstract class DeflateStreamTests
    {
        static string gzTestFile(String fileName) { return Path.Combine("GZTestData", fileName); }

        [Fact]
        public void BaseStream1()
        {
            var writeStream = new MemoryStream();
            var zip = new DeflateStream(writeStream, CompressionMode.Compress);

            Assert.Same(zip.BaseStream, writeStream);
            writeStream.Dispose();
        }

        [Fact]
        public void BaseStream2()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Decompress);

            Assert.Same(zip.BaseStream, ms);
            ms.Dispose();
        }

        [Fact]
        public async Task ModifyBaseStream()
        {
            var ms = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.txt.gz"));
            var newMs = StripHeaderAndFooter.Strip(ms);

            var zip = new DeflateStream(newMs, CompressionMode.Decompress);
            int size = 1024;
            Byte[] bytes = new Byte[size];
            zip.BaseStream.Read(bytes, 0, size); // This will throw if the underlying stream is not writeable as expected

            zip.BaseStream.Position = 0;
            await zip.BaseStream.ReadAsync(bytes, 0, size); 
        }

        [Fact]
        public void DecompressCanRead()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Decompress);

            Assert.True(zip.CanRead);

            zip.Dispose();
            Assert.False(zip.CanRead);
        }

        [Fact]
        public void CompressCanWrite()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Compress);
            Assert.True(zip.CanWrite);

            zip.Dispose();
            Assert.False(zip.CanWrite);
        }

        [Fact]
        public void CanDisposeBaseStream()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Compress);
            ms.Dispose(); // This would throw if this was invalid
        }

        [Fact]
        public void CanDisposeDeflateStream()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Compress);
            zip.Dispose();

            // Base Stream should be null after dispose 
            Assert.Null(zip.BaseStream);

            zip.Dispose(); // Should be a no-op
        }

        [Fact]
        public async Task CanReadBaseStreamAfterDispose()
        {
            var ms = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.txt.gz"));
            var newMs = StripHeaderAndFooter.Strip(ms);

            var zip = new DeflateStream(newMs, CompressionMode.Decompress, true);
            var baseStream = zip.BaseStream;
            zip.Dispose();

            int size = 1024;
            Byte[] bytes = new Byte[size];
            baseStream.Read(bytes, 0, size); // This will throw if the underlying stream is not writeable as expected

            baseStream.Position = 0;
            await baseStream.ReadAsync(bytes, 0, size); 
        }

        [Fact]
        public async Task DecompressWorks()
        {
            var compareStream = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.txt"));
            var gzStream = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.txt.gz"));

            await DecompressAsync(compareStream, gzStream);
        }

        [Fact]
        public async Task DecompressWorksWithBinaryFile()
        {
            var compareStream = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.doc"));
            var gzStream = await LocalMemoryStream.readAppFileAsync(gzTestFile("GZTestDocument.doc.gz"));

            await DecompressAsync(compareStream, gzStream);
        }

        // Making this async since regular read/write are tested below
        private async Task DecompressAsync(MemoryStream compareStream, MemoryStream gzStream)
        {
            var strippedMs = StripHeaderAndFooter.Strip(gzStream);

            var ms = new MemoryStream();
            var zip = new DeflateStream(strippedMs, CompressionMode.Decompress);

            var deflateStream = new MemoryStream();

            int _bufferSize = 1024;
            var bytes = new Byte[_bufferSize];
            bool finished = false;
            int retCount;
            while (!finished)
            {
                retCount = await zip.ReadAsync(bytes, 0, _bufferSize);

                if (retCount != 0)
                    await deflateStream.WriteAsync(bytes, 0, retCount);
                else
                    finished = true;
            }

            deflateStream.Position = 0;
            compareStream.Position = 0;

            byte[] compareArray = compareStream.ToArray();
            byte[] writtenArray = deflateStream.ToArray();

            Assert.Equal(compareArray.Length, writtenArray.Length);
            for (int i = 0; i < compareArray.Length; i++)
            {
                Assert.Equal(compareArray[i], writtenArray[i]);
            }
        }

        [Fact]
        public async Task DecompressFailsWithRealGzStream()
        {
            String[] files = { gzTestFile("GZTestDocument.doc.gz"), gzTestFile("GZTestDocument.txt.gz") };
            foreach (String fileName in files)
            {
                var baseStream = await LocalMemoryStream.readAppFileAsync(fileName);
                var zip = new DeflateStream(baseStream, CompressionMode.Decompress);
                int _bufferSize = 2048;
                var bytes = new Byte[_bufferSize];
                Assert.Throws<InvalidDataException>(() => { zip.Read(bytes, 0, _bufferSize); });
                zip.Dispose();
            }
        }

        [Fact]
        public void DisposedBaseStreamThrows()
        {
            var ms = new MemoryStream();
            ms.Dispose();
            Assert.Throws<ArgumentException>(() =>
            {
                var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            });

            Assert.Throws<ArgumentException>(() =>
            {
                var deflate = new DeflateStream(ms, CompressionMode.Compress);
            });
        }

        [Fact]
        public void ReadOnlyStreamThrowsOnCompress()
        {
            var ms = new LocalMemoryStream();
            ms.SetCanWrite(false);

            Assert.Throws<ArgumentException>(() =>
            {
                var gzip = new DeflateStream(ms, CompressionMode.Compress);
            });
        }

        [Fact]
        public void WriteOnlyStreamThrowsOnDecompress()
        {
            var ms = new LocalMemoryStream();
            ms.SetCanRead(false);

            Assert.Throws<ArgumentException>(() =>
            {
                var gzip = new DeflateStream(ms, CompressionMode.Decompress);
            });
        }

        [Fact]
        public void TestCtors()
        {
            CompressionLevel[] legalValues = new CompressionLevel[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression };

            foreach (CompressionLevel level in legalValues)
            {
                bool[] boolValues = new bool[] { true, false };

                foreach (bool remainsOpen in boolValues)
                {
                    TestCtor(level, remainsOpen);
                }
            }
        }

        [Fact]
        public void TestLevelOptimial()
        {
            TestCtor(CompressionLevel.Optimal);
        }

        [Fact]
        public void TestLevelNoCompression()
        {
            TestCtor(CompressionLevel.NoCompression);
        }

        [Fact]
        public void TestLevelFastest()
        {
            TestCtor(CompressionLevel.Fastest);
        }

        private static void TestCtor(CompressionLevel level, bool? leaveOpen = null)
        {
            //Create the DeflateStream
            int _bufferSize = 1024;
            var bytes = new Byte[_bufferSize];
            var baseStream = new MemoryStream(bytes, true);
            DeflateStream ds;

            if (leaveOpen == null)
            {
                ds = new DeflateStream(baseStream, level);
            }
            else
            {
                ds = new DeflateStream(baseStream, level, leaveOpen ?? false);
            }

            //Write some data and Close the stream
            String strData = "Test Data";
            var encoding = Encoding.UTF8;
            Byte[] data = encoding.GetBytes(strData);
            ds.Write(data, 0, data.Length);
            ds.Flush();
            ds.Dispose();

            if (leaveOpen != true)
            {
                //Check that Close has really closed the underlying stream
                Assert.Throws<ObjectDisposedException>(() => { baseStream.Write(bytes, 0, bytes.Length); });
            }

            //Read the data
            Byte[] data2 = new Byte[_bufferSize];
            baseStream = new MemoryStream(bytes, false);
            ds = new DeflateStream(baseStream, CompressionMode.Decompress);
            int size = ds.Read(data2, 0, _bufferSize - 5);

            //Verify the data roundtripped
            for (int i = 0; i < size + 5; i++)
            {
                if (i < data.Length)
                {
                    Assert.Equal(data[i], data2[i]);
                }
                else
                {
                    Assert.Equal(data2[i], (byte)0);
                }
            }
        }

        [Fact]
        public void CtorArgumentValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionLevel.Fastest));
            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionMode.Decompress));
            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionMode.Compress));

            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionLevel.Fastest, true));
            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionMode.Decompress, false));
            Assert.Throws<ArgumentNullException>(() => new DeflateStream(null, CompressionMode.Compress, true));

            Assert.Throws<ArgumentException>(() => new DeflateStream(new MemoryStream(), (CompressionMode)42));
            Assert.Throws<ArgumentException>(() => new DeflateStream(new MemoryStream(), (CompressionMode)43, true));

            Assert.Throws<ArgumentException>(() => new DeflateStream(new MemoryStream(new byte[1], writable: false), CompressionLevel.Optimal));
        }

        [Fact]
        public async Task Flush()
        {
            var ms = new MemoryStream();
            var ds = new DeflateStream(ms, CompressionMode.Compress);
            ds.Flush();
            await ds.FlushAsync();

            // Just ensuring Flush doesn't throw
        }

        [Fact]
        public void FlushFailsAfterDispose()
        {
            var ms = new MemoryStream();
            var ds = new DeflateStream(ms, CompressionMode.Compress);
            ds.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { ds.Flush(); });
        }

        [Fact]
        public async Task FlushAsyncFailsAfterDispose()
        {
            var ms = new MemoryStream();
            var ds = new DeflateStream(ms, CompressionMode.Compress);
            ds.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                await ds.FlushAsync();
            });
        }

        [Fact]
        public void TestSeekMethodsDecompress()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Decompress);

            Assert.False(zip.CanSeek, "CanSeek should be false");

            Assert.Throws<NotSupportedException>(delegate { long value = zip.Length; });
            Assert.Throws<NotSupportedException>(delegate { long value = zip.Position; });
            Assert.Throws<NotSupportedException>(delegate { zip.Position = 100L; });
            Assert.Throws<NotSupportedException>(delegate { zip.SetLength(100L); });
            Assert.Throws<NotSupportedException>(delegate { zip.Seek(100L, SeekOrigin.Begin); });
        }

        [Fact]
        public void TestSeekMethodsCompress()
        {
            var ms = new MemoryStream();
            var zip = new DeflateStream(ms, CompressionMode.Compress);

            Assert.False(zip.CanSeek, "CanSeek should be false");

            Assert.Throws<NotSupportedException>(delegate { long value = zip.Length; });
            Assert.Throws<NotSupportedException>(delegate { long value = zip.Position; });
            Assert.Throws<NotSupportedException>(delegate { zip.Position = 100L; });
            Assert.Throws<NotSupportedException>(delegate { zip.SetLength(100L); });
            Assert.Throws<NotSupportedException>(delegate { zip.Seek(100L, SeekOrigin.Begin); });
        }

        [Fact]
        public void ReadWriteArgumentValidation()
        {
            using (var ds = new DeflateStream(new MemoryStream(), CompressionMode.Compress))
            {
                Assert.Throws<ArgumentNullException>(() => ds.Write(null, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => ds.Write(new byte[1], -1, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => ds.Write(new byte[1], 0, -1));
                Assert.Throws<ArgumentException>(() => ds.Write(new byte[1], 0, 2));
                Assert.Throws<ArgumentException>(() => ds.Write(new byte[1], 1, 1));
                Assert.Throws<InvalidOperationException>(() => ds.Read(new byte[1], 0, 1));
                ds.Write(new byte[1], 0, 0);
            }
            using (var ds = new DeflateStream(new MemoryStream(), CompressionMode.Compress))
            {
                Assert.Throws<ArgumentNullException>(() => { ds.WriteAsync(null, 0, 0); });
                Assert.Throws<ArgumentOutOfRangeException>(() => { ds.WriteAsync(new byte[1], -1, 0); });
                Assert.Throws<ArgumentOutOfRangeException>(() => { ds.WriteAsync(new byte[1], 0, -1); });
                Assert.Throws<ArgumentException>(() => { ds.WriteAsync(new byte[1], 0, 2); });
                Assert.Throws<ArgumentException>(() => { ds.WriteAsync(new byte[1], 1, 1); });
                Assert.Throws<InvalidOperationException>(() => { ds.Read(new byte[1], 0, 1); });
            }

            using (var ds = new DeflateStream(new MemoryStream(), CompressionMode.Decompress))
            {
                Assert.Throws<ArgumentNullException>(() => ds.Read(null, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => ds.Read(new byte[1], -1, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => ds.Read(new byte[1], 0, -1));
                Assert.Throws<ArgumentException>(() => ds.Read(new byte[1], 0, 2));
                Assert.Throws<ArgumentException>(() => ds.Read(new byte[1], 1, 1));
                Assert.Throws<InvalidOperationException>(() => ds.Write(new byte[1], 0, 1));

                var data = new byte[1] { 42 };
                Assert.Equal(0, ds.Read(data, 0, 0));
                Assert.Equal(42, data[0]);
            }
            using (var ds = new DeflateStream(new MemoryStream(), CompressionMode.Decompress))
            {
                Assert.Throws<ArgumentNullException>(() => { ds.ReadAsync(null, 0, 0); });
                Assert.Throws<ArgumentOutOfRangeException>(() => { ds.ReadAsync(new byte[1], -1, 0); });
                Assert.Throws<ArgumentOutOfRangeException>(() => { ds.ReadAsync(new byte[1], 0, -1); });
                Assert.Throws<ArgumentException>(() => { ds.ReadAsync(new byte[1], 0, 2); });
                Assert.Throws<ArgumentException>(() => { ds.ReadAsync(new byte[1], 1, 1); });
                Assert.Throws<InvalidOperationException>(() => { ds.Write(new byte[1], 0, 1); });
            }
        }

        [Fact]
        public void Precancellation()
        {
            var ms = new MemoryStream();
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                Assert.True(ds.WriteAsync(new byte[1], 0, 1, new CancellationToken(true)).IsCanceled);
                Assert.True(ds.FlushAsync(new CancellationToken(true)).IsCanceled);
            }
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress, leaveOpen: true))
            {
                Assert.True(ds.ReadAsync(new byte[1], 0, 1, new CancellationToken(true)).IsCanceled);
            }
        }

        [Fact]
        public async Task RoundtripCompressDecompress()
        {
            await RoundtripCompressDecompress(useAsync: false, useGzip: false, chunkSize: 1, totalSize: 10, level: CompressionLevel.Fastest);
            await RoundtripCompressDecompress(useAsync: true,  useGzip: true,  chunkSize: 1024, totalSize: 8192, level: CompressionLevel.Optimal);
        }

        [OuterLoop]
        [Theory]
        [MemberData("RoundtripCompressDecompressOuterData")]
        public Task RoundtripCompressDecompressOuter(bool useAsync, bool useGzip, int chunkSize, int totalSize, CompressionLevel level)
        {
            return RoundtripCompressDecompress(useAsync, useGzip, chunkSize, totalSize, level);
        }

        public static IEnumerable<object[]> RoundtripCompressDecompressOuterData
        {
            get
            {
                foreach (bool useAsync in new[] { true, false }) // whether to use Read/Write or ReadAsync/WriteAsync
                {
                    foreach (bool useGzip in new[] { true, false }) // whether to add on gzip headers/footers
                    {
                        foreach (var level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.NoCompression }) // compression level
                        {
                            yield return new object[] { useAsync, useGzip, 1, 5, level }; // smallest possible writes
                            yield return new object[] { useAsync, useGzip, 1023, 1023*10, level }; // overflowing internal buffer
                            yield return new object[] { useAsync, useGzip, 1024*1024, 1024*1024, level }; // large single write
                        }
                    }
                }
            }
        }

        private async Task RoundtripCompressDecompress(bool useAsync, bool useGzip, int chunkSize, int totalSize, CompressionLevel level)
        {
            byte[] data = new byte[totalSize];
            new Random(42).NextBytes(data);

            var compressed = new MemoryStream();
            using (var compressor = useGzip ? (Stream)new GZipStream(compressed, level, true) : new DeflateStream(compressed, level, true))
            {
                for (int i = 0; i < data.Length; i += chunkSize) // not using CopyTo{Async} due to optimizations in MemoryStream's implementation that avoid what we're trying to test
                {
                    switch (useAsync)
                    {
                        case true: await compressor.WriteAsync(data, i, chunkSize); break;
                        case false: compressor.Write(data, i, chunkSize); break;
                    }
                }
            }
            compressed.Position = 0;

            var decompressed = new MemoryStream();
            using (var decompressor = useGzip ? (Stream)new GZipStream(compressed, CompressionMode.Decompress, true) : new DeflateStream(compressed, CompressionMode.Decompress, true))
            {
                if (useAsync)
                    decompressor.CopyTo(decompressed, chunkSize);
                else
                    await decompressor.CopyToAsync(decompressed, chunkSize, CancellationToken.None);
            }

            Assert.Equal<byte>(data, decompressed.ToArray());
        }

        [Fact]
        public void SequentialReadsOnMemoryStream_Return_SameBytes()
        {
            byte[] data = new byte[1024*10];
            new Random(42).NextBytes(data);

            var compressed = new MemoryStream();
            using (var compressor = new DeflateStream(compressed, CompressionMode.Compress, true))
            {
                for (int i = 0; i < data.Length; i += 1024)
                {
                    compressor.Write(data, i, 1024);
                }
            }
            compressed.Position = 0;

            var decompressed = new MemoryStream();
            using (var decompressor = new DeflateStream(compressed, CompressionMode.Decompress, true))
            {
                int i, j;
                byte[] array = new byte[100];
                byte[] array2 = new byte[100];

                // only read in the first 100 bytes
                decompressor.Read(array, 0, array.Length);
                for (i = 0; i < array.Length; i++)
                    Assert.Equal(data[i], array[i]);

                // read in the next 100 bytes and make sure nothing is missing
                decompressor.Read(array2, 0, array2.Length);
                for (j = 0; j < array2.Length; j++)
                    Assert.Equal(data[j], array[j]);
            }
        }

        [Fact]
        public async Task WrapNullReturningTasksStream()
        {
            using (var ds = new DeflateStream(new BadWrappedStream(BadWrappedStream.Mode.ReturnNullTasks), CompressionMode.Decompress))
                await Assert.ThrowsAsync<InvalidOperationException>(() => ds.ReadAsync(new byte[1024], 0, 1024));
        }

        [Fact]
        public async Task WrapStreamReturningBadReadValues()
        {
            using (var ds = new DeflateStream(new BadWrappedStream(BadWrappedStream.Mode.ReturnTooLargeCounts), CompressionMode.Decompress))
                Assert.Throws<InvalidDataException>(() => ds.Read(new byte[1024], 0, 1024));
            using (var ds = new DeflateStream(new BadWrappedStream(BadWrappedStream.Mode.ReturnTooLargeCounts), CompressionMode.Decompress))
                await Assert.ThrowsAsync<InvalidDataException>(() => ds.ReadAsync(new byte[1024], 0, 1024));

            using (var ds = new DeflateStream(new BadWrappedStream(BadWrappedStream.Mode.ReturnTooSmallCounts), CompressionMode.Decompress))
                Assert.Equal(0, ds.Read(new byte[1024], 0, 1024));
            using (var ds = new DeflateStream(new BadWrappedStream(BadWrappedStream.Mode.ReturnTooSmallCounts), CompressionMode.Decompress))
                Assert.Equal(0, await ds.ReadAsync(new byte[1024], 0, 1024));
        }

        private sealed class BadWrappedStream : Stream
        {
            public enum Mode
            {
                Default,
                ReturnNullTasks,
                ReturnTooSmallCounts,
                ReturnTooLargeCounts,
            }

            private readonly Mode _mode;

            public BadWrappedStream(Mode mode) { _mode = mode; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                switch (_mode)
                {
                    case Mode.ReturnTooSmallCounts: return -1;
                    case Mode.ReturnTooLargeCounts: return buffer.Length + 1;
                    default: return 0;
                }
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return _mode == Mode.ReturnNullTasks ? 
                    null :
                    base.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return _mode == Mode.ReturnNullTasks ?
                   null :
                   base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void Write(byte[] buffer, int offset, int count) { }
            public override void Flush() { }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }

    }
}
