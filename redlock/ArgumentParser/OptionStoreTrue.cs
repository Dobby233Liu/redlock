using System;
using System.Collections.Generic;

namespace redlock.ArgumentParser;

internal class OptionStoreTrue : OptionBehavior
{
	internal override int MaxExtraParams { get; } = 0;

	internal override object? Parse(IList<string> extraParams)
	{
		return true;
	}

	internal override string[] Build(string optionName, object value)
	{
		if (value is not bool)
			throw new ArgumentException("Option value is not a bool", nameof(value));
		return value is true ? [optionName] : [];
	}
}