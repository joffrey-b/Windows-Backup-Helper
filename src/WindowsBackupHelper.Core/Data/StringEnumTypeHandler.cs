using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Data;

/// <summary>
/// Maps an enum to/from the TEXT representation used by the schema's CHECK(... IN (...))
/// columns (e.g. ChecksumMode.VerifyAgainstManifest &lt;-&gt; "VerifyAgainstManifest"), so the
/// database stores self-documenting values instead of opaque integers.
/// </summary>
public sealed class StringEnumTypeHandler<T> : SqlMapper.TypeHandler<T>
    where T : struct, Enum
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = value.ToString();
    }

    public override T Parse(object value)
    {
        return Enum.Parse<T>((string)value);
    }
}

public static class DapperTypeHandlers
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<ChecksumMode>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<ExclusionScope>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<ExclusionPatternType>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<ExclusionTargetType>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<RunTriggerType>());
        SqlMapper.AddTypeHandler(new StringEnumTypeHandler<RunOutcome>());

        _registered = true;
    }
}
