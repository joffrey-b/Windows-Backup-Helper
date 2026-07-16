using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class CredentialTargetRepository(IDbConnection connection)
{
    public Task<CredentialTarget?> GetByIdAsync(string id) =>
        connection.QuerySingleOrDefaultAsync<CredentialTarget>(
            "SELECT * FROM CredentialTarget WHERE Id = @Id", new { Id = id });

    public async Task<IReadOnlyList<CredentialTarget>> GetAllAsync() =>
        (await connection.QueryAsync<CredentialTarget>("SELECT * FROM CredentialTarget ORDER BY Label")).AsList();

    public Task InsertAsync(CredentialTarget target) =>
        connection.ExecuteAsync(
            """
            INSERT INTO CredentialTarget (Id, Label, HostOrUncRoot, CredentialManagerTargetName)
            VALUES (@Id, @Label, @HostOrUncRoot, @CredentialManagerTargetName)
            """,
            target);

    public Task UpdateAsync(CredentialTarget target) =>
        connection.ExecuteAsync(
            """
            UPDATE CredentialTarget SET Label = @Label, HostOrUncRoot = @HostOrUncRoot,
                CredentialManagerTargetName = @CredentialManagerTargetName
            WHERE Id = @Id
            """,
            target);

    public Task DeleteAsync(string id) =>
        connection.ExecuteAsync("DELETE FROM CredentialTarget WHERE Id = @Id", new { Id = id });
}
