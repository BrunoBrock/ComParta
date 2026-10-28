using ComParta.Core.Ports;
using ComParta.Core.UseCases;

namespace ComParta.Core.Tests;

public sealed class AboutUseCasesTests
{
    private sealed class Notices(string? text) : IThirdPartyNotices
    {
        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(text);
    }

    [Fact]
    public async Task Gives_the_notices_with_plain_line_endings()
    {
        var about = new AboutUseCases(new Notices("react 19.0.0\r\nLicense: MIT\r\n"));

        Assert.Equal("react 19.0.0\nLicense: MIT\n", await about.NoticesAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task Gives_nothing_when_the_file_is_missing_or_empty(string? text)
    {
        Assert.Null(await new AboutUseCases(new Notices(text)).NoticesAsync());
    }
}
