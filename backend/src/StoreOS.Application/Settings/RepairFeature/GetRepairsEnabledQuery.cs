using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Settings.RepairFeature;

public record GetRepairsEnabledQuery : IRequest<bool>, IReadOnlyRequest;
