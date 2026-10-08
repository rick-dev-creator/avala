using System.Reflection;
using System.Runtime.CompilerServices;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Avala.Sdk.Events;

namespace Avala.ArchitectureTests.Ddd;

internal static class BuildingBlocks
{
    extension(Type type)
    {
        public bool IsAggregate => type.AggregateIdentifier is not null;

        public Type? AggregateIdentifier =>
            type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IAggregateRoot<>))
                .Select(contract => contract.GetGenericArguments()[0])
                .FirstOrDefault();

        public bool IsResult => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<,>);

        public bool IsDomainEvent => typeof(IDomainEvent).IsAssignableFrom(type);

        public bool IsIntegrationEvent => typeof(IIntegrationEvent).IsAssignableFrom(type);

        public bool IsRecordClass =>
            type.IsClass && type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

        public bool IsRecordStruct =>
            type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

        public bool IsReadOnlyStruct => type.IsDefined(typeof(IsReadOnlyAttribute), false);
    }

    public const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
