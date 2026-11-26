using MediatR;

namespace GridMeter.Application.Abstractions;

/// <summary>A write. Runs inside a DB transaction that also persists outbox rows.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

public interface IQuery<out TResponse> : IRequest<TResponse>;
