## ADDED Requirements

### Requirement: Encrypted columns are bound to their column

The system SHALL encrypt every encrypted column value with AES-256-GCM in the envelope `tpenc:2:{keyVersion}:{nonce}:{tag}:{ciphertext}`, with the stable `table.column` name as associated data. A ciphertext moved into a different encrypted column SHALL fail to decrypt. The legacy envelope `tpenc:{keyVersion}:{nonce}:{tag}:{ciphertext}`, which has no associated data, SHALL still decrypt.

#### Scenario: Ciphertext moved between columns

- **GIVEN** a Guild API token encrypted for `guilds.guild_api_token`
- **WHEN** that ciphertext is read as `tacticus_integrations.tacticus_api_key`
- **THEN** decryption fails

### Requirement: Lookup hashes use a derived subkey

Lookup hashes SHALL be HMAC-SHA256 under an HKDF-SHA256 subkey of the current encryption key, never under the encryption key itself.

#### Scenario: Hash key differs from the encryption key

- **WHEN** a Tacticus user id is hashed
- **THEN** the hash equals HMAC-SHA256 under the HKDF subkey, not under the raw AES key

### Requirement: Encryption options are validated at startup

The service SHALL NOT start unless the column-encryption options are valid:

- the current key version names a configured key;
- every configured key decodes to 32 bytes.

#### Scenario: Misconfigured key

- **WHEN** the service starts with `ColumnEncryption:CurrentKeyVersion` naming a missing key
- **THEN** startup fails with the column-encryption validation message

### Requirement: Legacy rows are rewritten once at startup

After migrations apply on startup, a backfill SHALL re-encrypt every encrypted value not yet on the version-2 envelope, and SHALL recompute its row's lookup hash with the subkey. It SHALL run in one transaction behind a database advisory lock, so replicas serialize. A later run SHALL find nothing to rewrite.

#### Scenario: Startup after upgrade

- **GIVEN** a database whose profiles, integrations, guilds and guild members are on the legacy envelope
- **WHEN** the service starts
- **THEN** every such value is on the `tpenc:2:` envelope, every profile and member hash still links the same rows, and a restart rewrites nothing
