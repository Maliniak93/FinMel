using FinMel.Domain.Abstractions;

namespace FinMel.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        // Arrange & Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Success_WithValue_ShouldCreateSuccessfulResultWithValue()
    {
        // Arrange
        var expectedValue = "test value";

        // Act
        var result = Result.Success(expectedValue);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_ShouldCreateFailedResult()
    {
        // Arrange
        var error = new TestError();

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Create_WithNullValue_ShouldReturnFailure()
    {
        // Arrange & Act
        var result = Result.Create<string>(null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(Error.NullValue, result.Error);
    }

    [Fact]
    public void Create_WithValidValue_ShouldReturnSuccess()
    {
        // Arrange
        var value = "valid value";

        // Act
        var result = Result.Create(value);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void Value_OnFailureResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Failure<string>(new TestError());

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    private sealed class TestError : Error
    {
        public TestError() : base("Test.Error", "Test Error", "This is a test error")
        {
        }
    }
}
