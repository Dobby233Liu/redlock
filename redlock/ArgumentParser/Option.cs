using System;

namespace redlock.ArgumentParser;

[AttributeUsage(AttributeTargets.Property)]
internal class Option(string name) : Attribute
{
	/// <remarks>
	///     This is always treated case-insensitively, since we have no business logic that relies on the sensitivity
	/// </remarks>
	public string Name { get; } = name.ToLower();
}