using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGG.Core.Content;

/// <summary>
/// A vector in car space: meters, +X = car front, +Y = up, +Z = car right.
/// Stored in JSON as <c>[x, y, z]</c>.
/// </summary>
[JsonConverter(typeof(Vec3JsonConverter))]
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 1, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized()
    {
        double l = Length;
        return l > 1e-12 ? this * (1 / l) : Zero;
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

public sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A vector must be an array [x, y, z].");
        double x = 0, y = 0, z = 0;
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            double v = reader.GetDouble();
            if (i == 0) x = v;
            else if (i == 1) y = v;
            else if (i == 2) z = v;
            i++;
        }
        if (i != 3) throw new JsonException($"A vector needs 3 numbers, got {i}.");
        return new Vec3(x, y, z);
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
