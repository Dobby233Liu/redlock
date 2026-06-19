using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace redlock;

[AttributeUsage(AttributeTargets.Property)]
internal class ArgumentName(string name) : Attribute
{
	public string Name { get; } = name.ToLower();
}

internal abstract class ArgumentsBase
{
	public ArgumentsBase()
	{
	}
	
	public ArgumentsBase(string[] args)
	{
		args = args.Select(i => i.ToLower()).ToArray();
		
		var props = this.GetType().GetProperties();
		foreach (var prop in props)
		{
			var attr = prop.GetCustomAttribute<ArgumentName>();
			prop.SetValue(this, attr != null && args.Contains(attr.Name));
		}
	}

	public string Build()
	{
		List<string> args = [];
		
		var props = this.GetType().GetProperties();
		foreach (var prop in props)
		{
			var attr = prop.GetCustomAttribute<ArgumentName>();
			if (attr != null && (bool)prop.GetValue(this))
				args.Add(attr.Name);
		}
		
		return string.Join(" ", args);
	}
}