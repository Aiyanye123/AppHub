using System;

namespace CommunityToolkit.Mvvm.ComponentModel;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ObservablePropertyAttribute : Attribute
{
}
