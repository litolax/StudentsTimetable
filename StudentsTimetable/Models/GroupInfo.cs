namespace StudentsTimetable.Models;

public class GroupInfo
{
    public int Number { get; set; }
    public string Date { get; set; }
    public List<Lesson> Lessons = new();
}