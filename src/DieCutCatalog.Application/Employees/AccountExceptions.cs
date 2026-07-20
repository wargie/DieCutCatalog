namespace DieCutCatalog.Application.Employees;

public sealed class DuplicateEmailException(string email)
    : Exception($"An account with email '{email}' already exists.");

public sealed class EmailDeliveryUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class InvalidSetupTokenException()
    : Exception("The initial administrator setup token is invalid.");

public sealed class SetupAlreadyCompletedException()
    : Exception("The initial administrator has already been created.");
