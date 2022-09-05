using MongoDB.Bson;

namespace StudentsTimetable.Models;

public class Timetable
{
    public ObjectId Id { get; set; }
    public string Date { get; set; } = "";
}