/**
 * SQL query constants for Logo operations.
 * Centralizes all logo-related queries to avoid duplication.
 */
export class LogoQueries {
  /**
   * Check if logo exists for source.
   */
  public static readonly GET_ID_FOR_SOURCE = `
    SELECT Id FROM Logos
    WHERE Source = ? AND IsDeleted = 0
    LIMIT 1`;

  /**
   * Look up a logo by source regardless of IsDeleted state. Used by getOrCreate to detect
   * soft-deleted rows that still occupy the UNIQUE(Source) slot, so we can refill them
   * instead of trying (and failing) to INSERT a duplicate.
   */
  public static readonly GET_BY_SOURCE_INCLUDING_DELETED = `
    SELECT Id, IsDeleted FROM Logos
    WHERE Source = ?
    LIMIT 1`;

  /**
   * Insert new logo.
   */
  public static readonly INSERT = `
    INSERT INTO Logos (Id, Source, FileData, CreatedAt, UpdatedAt, IsDeleted)
    VALUES (?, ?, ?, ?, ?, ?)`;

  /**
   * Restore a soft-deleted logo and refill its bytes in one statement. Caller passes
   * (FileData, UpdatedAt, Id).
   */
  public static readonly RESTORE_WITH_FILE_DATA = `
    UPDATE Logos
    SET IsDeleted = 0,
        FileData = ?,
        UpdatedAt = ?
    WHERE Id = ?`;
}
