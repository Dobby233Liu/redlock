using System;
using System.Collections.Generic;

namespace redlock.ArgumentParser;

[AttributeUsage(AttributeTargets.Property)]
internal abstract class OptionBehavior : Attribute
{
	/// <summary>
	///     If smaller than 0 there is no restriction
	/// </summary>
	internal virtual int MaxExtraParams { get; } = -1;

	/// <param name="extraParams">List of additional parameters</param>
	internal abstract object? Parse(IList<string> extraParams);

	internal abstract string[] Build(string optionName, object value);
}