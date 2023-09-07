namespace StudentsTimetable.Models;

public class GroupInfo
{
    public int Number { get; set; }
    public string Date { get; set; }
    public List<Lesson> Lessons = new();

    public override bool Equals(object? obj)
    {
        if (obj == null || this.GetType() != obj.GetType())
        {
            return false;
        }

        GroupInfo other = (GroupInfo)obj;

        return this.Number == other.Number && this.Date == other.Date && this.Lessons.SequenceEqual(other.Lessons);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Number, this.Date, this.Lessons);
    }
}