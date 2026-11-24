using PANiXiDA.Core.Domain.Identifiers;

namespace Samples.Catalog.Domain.Models.Foreign;

public readonly record struct Id(Guid Value) : IStronglyTypedId;
