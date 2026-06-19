using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace redlock;

// This file implements a crippled and not-very-good argument parser
// I will probably make it slightly more user-friendly at some point

[AttributeUsage(AttributeTargets.Property)]
internal class Option(string name) : Attribute
{
	/// <remarks>
	/// This is always treated case-insensitively, since we have no business logic that relies on the sensitivity
	/// </remarks>
	public string Name { get; } = name.ToLower();
}

[AttributeUsage(AttributeTargets.Property)]
internal abstract class OptionBehavior : Attribute
{
	/// <summary>
	/// If smaller than 0 there is no restriction
	/// </summary>
	internal virtual int MaxExtraParams { get; } = -1;

	/// <param name="extraParams">Enumerator for obtaining additional parameters, if applicable</param>
	internal abstract object Parse(IList<string> extraParams);

	internal abstract string[] Build(string optionName, object value);
}

internal class OptionStoreTrue : OptionBehavior
{
	internal override int MaxExtraParams { get; } = 0;

	internal override object Parse(IList<string> extraParams)
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

internal abstract class ArgumentsBase
{
	// ReSharper disable once InconsistentNaming
	/// <remarks>1st entry will be the prefix used by <see cref="Build"/></remarks>
	private static readonly string[] _optionPrefixes = [ "/", "-" ];
	
	private Dictionary<string, PropertyInfo> _props;
	private Dictionary<PropertyInfo, OptionBehavior> _behaviors;
	
	private void InitPropertyCache()
	{
		_props = new Dictionary<string, PropertyInfo>();
		_behaviors = new Dictionary<PropertyInfo, OptionBehavior>();
		
		foreach (var prop in GetType().GetProperties())
		{
			var attr = prop.GetCustomAttribute<Option>();
			if (attr is null) continue;
			_props.Add(attr.Name, prop);
			
			var behavior = prop.GetCustomAttribute<OptionBehavior>();
			if (behavior is null)
				throw new ArgumentException($"Option {attr.Name} doesn't have a OptionBehavior attribute");
			_behaviors.Add(prop, behavior);
		}
	}
	
	public ArgumentsBase()
	{
		InitPropertyCache();
	}

	// stuff like /K:v is kind of a non-goal for now
	private static bool TryParseOptionName(string arg, out string option)
	{
		bool UsesPrefix(string arg, string prefix) => arg.StartsWith(prefix) && arg.Length > prefix.Length;
		string ParseName(string arg, string prefix) => arg.Substring(prefix.Length).ToLower();

		var firstPrefix = _optionPrefixes[0];
		if (UsesPrefix(arg, firstPrefix))
		{
			option = ParseName(arg, firstPrefix);
			return true;
		}

		foreach (var prefix in _optionPrefixes.Skip(1).OrderByDescending(x => x.Length))
		{
			if (!UsesPrefix(arg, prefix)) continue;
			option = ParseName(arg, prefix);
			return true;
		}
		
		option = null;
		return false;
	}
	
	public ArgumentsBase(IEnumerable<string> args)
	{
		InitPropertyCache();
		
		Dictionary<string, List<string>> argsByOption = new Dictionary<string, List<string>>();
		string curOption = null;
		foreach (var arg in args)
		{
			if (TryParseOptionName(arg, out var thisOption))
			{
				curOption = thisOption;
				continue;
			}

			// currently a non-goal
			if (string.IsNullOrEmpty(curOption))
				throw new ArgumentException($"This application doesn't take positional arguments");
			
			if (!argsByOption.ContainsKey(curOption))
				argsByOption[curOption] = new();
			argsByOption[curOption].Add(arg);
		}

		var freshInstance = Activator.CreateInstance(GetType());
		foreach (var entry in argsByOption.OrderBy(x => x.Key))
		{
			if (!(_props.TryGetValue(entry.Key, out var prop)
			    && _behaviors.TryGetValue(prop, out var behavior)))
				throw new ArgumentException($"Unknown option {entry.Key}");
			
			if (behavior.MaxExtraParams >= 0 && entry.Value.Count > behavior.MaxExtraParams)
				throw new ArgumentException($"Option {entry.Key} takes at most {behavior.MaxExtraParams} parameters, {entry.Value.Count} given");
			
			var value = behavior.Parse(entry.Value);
			prop.SetValue(this, value ?? prop.GetValue(freshInstance));
		}
	}

	public string Build()
	{
		List<string> args = [];
		foreach (var entry in _props.OrderBy(prop => prop.Key))
		{
			var attrName = entry.Key;
			var prop = entry.Value;
			
			if (!_behaviors.TryGetValue(prop, out var behavior)) continue;
			args.AddRange(behavior.Build(_optionPrefixes[0] + attrName, prop.GetValue(this)));
		}
		return string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg));
	}
}