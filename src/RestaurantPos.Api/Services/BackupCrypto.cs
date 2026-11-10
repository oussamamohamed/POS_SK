using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantPos.Api.Services;

/// <summary>
/// Format `.posbak`, identique à `LocalBackupCrypto.swift` (iPad) : <c>magic(8) | itérations PBKDF2 (UInt32 BE) | sel(16) | nonce(12) | texte chiffré | tag(16)</c>.
/// L'en-tête est authentifié (AAD) ; la clé dérive de la phrase (NFC, UTF-8) par PBKDF2-HMAC-SHA256.
/// </summary>
public static class BackupCrypto
{
    public const int DefaultIterations = 200_000;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 8 + 4 + SaltLength;
    private const int MaxIterations = 10_000_000;

    private static ReadOnlySpan<byte> Magic => "POSBAK1\n"u8;

    public static byte[] Seal(byte[] plain, string passphrase, int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);
        var output = new byte[HeaderLength + NonceLength + plain.Length + TagLength];
        Magic.CopyTo(output);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8, 4), (uint)iterations);
        RandomNumberGenerator.Fill(output.AsSpan(12, SaltLength));
        RandomNumberGenerator.Fill(output.AsSpan(HeaderLength, NonceLength));
        using var aes = new AesGcm(DeriveKey(passphrase, output.AsSpan(12, SaltLength), iterations), TagLength);
        aes.Encrypt(
            output.AsSpan(HeaderLength, NonceLength), plain,
            output.AsSpan(HeaderLength + NonceLength, plain.Length), output.AsSpan(HeaderLength + NonceLength + plain.Length, TagLength),
            output.AsSpan(0, HeaderLength));
        return output;
    }

    public static byte[] Open(byte[] file, string passphrase)
    {
        if (file.Length < HeaderLength + NonceLength + TagLength || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Ce fichier n'est pas une sauvegarde de l'application.");
        }
        uint iterations = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(8, 4));
        if (iterations is 0 or > MaxIterations)
        {
            throw new InvalidDataException("Ce fichier n'est pas une sauvegarde de l'application.");
        }
        int cipherLength = file.Length - HeaderLength - NonceLength - TagLength;
        var plain = new byte[cipherLength];
        using var aes = new AesGcm(DeriveKey(passphrase, file.AsSpan(12, SaltLength), (int)iterations), TagLength);
        try
        {
            aes.Decrypt(
                file.AsSpan(HeaderLength, NonceLength), file.AsSpan(HeaderLength + NonceLength, cipherLength),
                file.AsSpan(HeaderLength + NonceLength + cipherLength, TagLength), plain, file.AsSpan(0, HeaderLength));
        }
        catch (CryptographicException)
        {
            // Phrase fausse et fichier altéré ne se distinguent pas avec AES-GCM.
            throw new InvalidDataException("Phrase de sauvegarde incorrecte ou fichier altéré.");
        }
        return plain;
    }

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC)), salt, iterations, HashAlgorithmName.SHA256, 32);
}
