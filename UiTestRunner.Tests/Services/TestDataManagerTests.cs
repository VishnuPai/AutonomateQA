using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using UiTestRunner.Services;
using System.Collections.Generic;

namespace UiTestRunner.Tests.Services
{
    public class TestDataManagerTests
    {
        private readonly Mock<IWebHostEnvironment> _mockEnv;
        private readonly Mock<ILogger<TestDataManager>> _mockLogger;

        public TestDataManagerTests()
        {
            _mockEnv = new Mock<IWebHostEnvironment>();
            _mockLogger = new Mock<ILogger<TestDataManager>>();
            
            _mockEnv.Setup(e => e.ContentRootPath).Returns("/test");
        }

        private IConfiguration CreateConfig(Dictionary<string, string?> secrets)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(secrets)
                .Build();
        }

        [Fact]
        public void ReplacePlaceholders_WithPlaceholder_ReplacesValue()
        {
            // Arrange
            var config = CreateConfig(new Dictionary<string, string?>());
            var manager = new TestDataManager(config, _mockEnv.Object, _mockLogger.Object);
            manager.AddSecret("Username", "testuser");

            // Act
            var result = manager.ReplacePlaceholders("Hello {{Username}}");

            // Assert
            Assert.Equal("Hello testuser", result);
        }

        [Fact]
        public void ReplacePlaceholders_WithoutPlaceholder_ReturnsOriginal()
        {
            // Arrange
            var config = CreateConfig(new Dictionary<string, string?>());
            var manager = new TestDataManager(config, _mockEnv.Object, _mockLogger.Object);

            // Act
            var result = manager.ReplacePlaceholders("Hello World");

            // Assert
            Assert.Equal("Hello World", result);
        }

        [Fact]
        public void MaskLiterals_WithSecret_ReplacesWithPlaceholder()
        {
            // Arrange
            var config = CreateConfig(new Dictionary<string, string?>());
            var manager = new TestDataManager(config, _mockEnv.Object, _mockLogger.Object);
            manager.AddSecret("Password", "secret123");

            // Act
            var result = manager.MaskLiterals("Password is secret123");

            // Assert
            Assert.Equal("Password is {{Password}}", result);
        }

        [Fact]
        public void GetValue_WithScopedSecret_ReturnsValue()
        {
            // Arrange
            var config = CreateConfig(new Dictionary<string, string?>());
            var manager = new TestDataManager(config, _mockEnv.Object, _mockLogger.Object);
            manager.AddSecret("Key", "Value");

            // Act
            var result = manager.GetValue("Key");

            // Assert
            Assert.Equal("Value", result);
        }

        [Fact]
        public void GetValue_WithoutSecret_ReturnsKey()
        {
            // Arrange
            var config = CreateConfig(new Dictionary<string, string?>());
            var manager = new TestDataManager(config, _mockEnv.Object, _mockLogger.Object);

            // Act
            var result = manager.GetValue("NonExistentKey");

            // Assert
            Assert.Equal("NonExistentKey", result);
        }
    }
}
