using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests.Domain.Models.Foreign;

public readonly record struct Id(Guid Value) : IStronglyTypedId;
