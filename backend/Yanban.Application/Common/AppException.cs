namespace Yanban.Application.Common;

/// <summary>
/// Base type for expected application errors. The API maps StatusCode to the
/// HTTP response (see ExceptionHandlingMiddleware), so handlers can throw these
/// instead of threading result objects everywhere.
/// </summary>
public abstract class AppException : Exception
{
    public abstract int StatusCode { get; }
    protected AppException(string message) : base(message) { }
}

public sealed class ValidationAppException : AppException
{
    public override int StatusCode => 400;
    public ValidationAppException(string message) : base(message) { }
}

public sealed class UnauthorizedAppException : AppException
{
    public override int StatusCode => 401;
    public UnauthorizedAppException(string message) : base(message) { }
}

public sealed class ForbiddenAppException : AppException
{
    public override int StatusCode => 403;
    public ForbiddenAppException(string message) : base(message) { }
}

public sealed class NotFoundAppException : AppException
{
    public override int StatusCode => 404;
    public NotFoundAppException(string message) : base(message) { }
}

public sealed class ConflictAppException : AppException
{
    public override int StatusCode => 409;
    public ConflictAppException(string message) : base(message) { }
}
