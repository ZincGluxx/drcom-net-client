using System.Text.Json.Serialization;
using DrComCampus.Models;

namespace DrComCampus.Helpers;

[JsonSerializable(typeof(AppConfiguration))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
