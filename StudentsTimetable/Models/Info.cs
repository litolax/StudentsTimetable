using MongoDB.Bson;

namespace StudentsTimetable.Models;

public class Info
{
    public ObjectId Id { get; set; }
    public bool ParseAllowed { get; set; }
    public bool LoadFixFile { get; set; }
}