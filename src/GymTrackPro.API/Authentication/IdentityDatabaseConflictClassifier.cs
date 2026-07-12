using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Authentication;

public static class IdentityDatabaseConflictClassifier
{
    public const int SqlServerUniqueIndexViolation = 2601;
    public const int SqlServerUniqueConstraintViolation = 2627;

    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error =>
                    IsUniqueViolationNumber(error.Number)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUniqueViolationNumber(int sqlServerErrorNumber) =>
        sqlServerErrorNumber is SqlServerUniqueIndexViolation
            or SqlServerUniqueConstraintViolation;
}
