using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace StudentsTimetable.Models;

public class Day
{
    public ObjectId Id { get; set; }
    public string Date { get; set; }
    [BsonIgnore] public List<GroupInfo> GroupInfos = new();
}