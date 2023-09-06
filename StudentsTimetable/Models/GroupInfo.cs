namespace StudentsTimetable.Models;

public class GroupInfo
{
    public int Number { get; set; }
    public string Date { get; set; }
    public List<Lesson> Lessons = new();

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        GroupInfo other = (GroupInfo)obj;

        return Number == other.Number &&
               Date == other.Date &&
               Lessons.SequenceEqual(other.Lessons);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Number, Date, Lessons);
    }
}