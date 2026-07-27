namespace Skarbiec.Contracts.Tests;

public sealed class ResultTests
{
    private static readonly Error SampleError = new("Sample.Error", "Something went wrong.");

    [Fact]
    public void Success_IsSuccessAndCarriesNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailureAndCarriesTheError()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesTheValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccessingValueThrows()
    {
        var result = Result<int>.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result<int> result = SampleError;

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }
}
