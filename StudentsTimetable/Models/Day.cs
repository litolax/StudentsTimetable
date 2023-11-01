using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace StudentsTimetable.Models;

public class Day
{
    public string Date { get; set; }
    [BsonIgnore] public List<GroupInfo> GroupInfos { get; set; } = new();
}