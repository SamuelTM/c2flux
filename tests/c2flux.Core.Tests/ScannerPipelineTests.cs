using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace c2flux.Core.Tests
{
    public class ScannerPipelineTests
    {
        [Fact]
        public async Task Uses_the_first_supported_scanner_that_succeeds()
        {
            FakeScanner unsupported = new FakeScanner("Unsupported", supported: false);
            FakeScanner failing = new FakeScanner("Failing", fail: true);
            FakeScanner working = new FakeScanner("Working");
            FakeScanner unused = new FakeScanner("Unused");
            List<string> statusKeys = new List<string>();

            ScannerPipeline pipeline = new ScannerPipeline(new[] { unsupported, failing, working, unused });
            FileSystemEntry result = await pipeline.ScanAsync("root", null, CancellationToken.None, default, statusKeys.Add);

            Assert.Equal("Working", result.Name);
            Assert.Equal(0, unsupported.ScanCount);
            Assert.Equal(1, failing.ScanCount);
            Assert.Equal(1, working.ScanCount);
            Assert.Equal(0, unused.ScanCount);
            Assert.Equal(new[] { "Status.Failing", "Status.Working" }, statusKeys);
        }

        [Fact]
        public async Task The_last_supported_scanners_failure_is_the_scans_failure()
        {
            ScannerPipeline pipeline = new ScannerPipeline(new[]
            {
                new FakeScanner("First", fail: true),
                new FakeScanner("Last", fail: true),
                new FakeScanner("Unsupported", supported: false),
            });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.ScanAsync("root", null, CancellationToken.None, default));

            Assert.Equal("Last failed", exception.Message);
        }

        [Fact]
        public async Task Cancellation_is_not_treated_as_a_failure()
        {
            FakeScanner next = new FakeScanner("Next");
            ScannerPipeline pipeline = new ScannerPipeline(new[] { new FakeScanner("Canceled", cancel: true), next });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pipeline.ScanAsync("root", null, CancellationToken.None, default));

            Assert.Equal(0, next.ScanCount);
        }

        [Fact]
        public async Task No_supported_scanner_is_an_error()
        {
            ScannerPipeline pipeline = new ScannerPipeline(new[] { new FakeScanner("Unsupported", supported: false) });

            await Assert.ThrowsAsync<NotSupportedException>(
                () => pipeline.ScanAsync("root", null, CancellationToken.None, default));
        }

        private sealed class FakeScanner : IFileSystemScanner
        {
            private readonly bool _supported;
            private readonly bool _fail;
            private readonly bool _cancel;

            public FakeScanner(string name, bool supported = true, bool fail = false, bool cancel = false)
            {
                Name = name;
                _supported = supported;
                _fail = fail;
                _cancel = cancel;
            }

            public string Name { get; }
            public string StatusTextKey => "Status." + Name;
            public string FailureAlertKey => "Alert." + Name;
            public int ScanCount { get; private set; }

            public ScannerSupport GetSupport(string rootPath)
            {
                return _supported ? ScannerSupport.Supported : ScannerSupport.NotSupported("fake");
            }

            public Task<FileSystemEntry> ScanAsync(
                string rootPath,
                IProgress<ScanProgress> progress,
                CancellationToken cancellationToken,
                PauseToken pauseToken)
            {
                ScanCount++;

                if (_cancel)
                {
                    throw new OperationCanceledException();
                }

                if (_fail)
                {
                    throw new InvalidOperationException(Name + " failed");
                }

                return Task.FromResult(new FileSystemEntry { Name = Name, FullPath = rootPath, IsDirectory = true });
            }
        }
    }
}
