using System.Text.Json.Serialization;
using CampusNetworkLogin.Models;

namespace CampusNetworkLogin.Helpers;

[JsonSerializable(typeof(ConfigModel))]
public partial class AppJsonContext : JsonSerializerContext
{
}
