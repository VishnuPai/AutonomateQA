using Xunit;
using UiTestRunner.Helpers;

namespace UiTestRunner.Tests.Helpers
{
    public class UrlValidatorTests
    {
        [Theory]
        [InlineData("https://example.com", true)]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com/path", true)]
        [InlineData("https://example.com:443/path", true)]
        public void IsSafeUrl_ValidUrls_ReturnsTrue(string url, bool expected)
        {
            // Act
            var result = UrlValidator.IsSafeUrl(url);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("localhost")]
        [InlineData("http://localhost")]
        [InlineData("http://127.0.0.1")]
        [InlineData("http://::1")]
        [InlineData("http://0.0.0.0")]
        [InlineData("http://10.0.0.1")]
        [InlineData("http://192.168.1.1")]
        [InlineData("http://172.16.0.1")]
        [InlineData("http://169.254.169.254")]
        [InlineData("ftp://example.com")]
        [InlineData("file:///path/to/file")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not-a-url")]
        public void IsSafeUrl_InvalidOrBlockedUrls_ReturnsFalse(string? url)
        {
            // Act
            var result = UrlValidator.IsSafeUrl(url!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsSafeUrl_PrivateSubnet_ReturnsFalse()
        {
            // Arrange
            var privateIps = new[]
            {
                "http://10.0.0.1",
                "http://192.168.1.1",
                "http://172.16.0.1",
                "http://172.31.255.255"
            };

            // Act & Assert
            foreach (var ip in privateIps)
            {
                Assert.False(UrlValidator.IsSafeUrl(ip), $"Should block private IP: {ip}");
            }
        }
    }
}
