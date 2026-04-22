using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace NGB.PostgreSql.Dapper;

/// <summary>
/// Dapper type handler for <see cref="DateOnly"/>.
/// Dapper doesn't support DateOnly out of the box; we map it to a PostgreSQL <c>date</c>.
/// </summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        // Prefer native Npgsql type when available
        if (parameter is NpgsqlParameter npgsql)
        {
            npgsql.NpgsqlDbType = NpgsqlDbType.Date;
            npgsql.Value = value.ToDateTime(TimeOnly.MinValue);
            return;
        }

        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        DateOnly d => d,
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };
}
