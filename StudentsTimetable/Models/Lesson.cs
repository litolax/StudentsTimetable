namespace StudentsTimetable.Models;

public class Lesson
{
    public int Number { get; set; }
    public string Cabinet { get; set; }
    public string Group { get; set; }
    public string Name { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType() || obj is not Lesson other)
        {
            return false;
        }

        return Number == other.Number &&
               Cabinet == other.Cabinet &&
               Group == other.Group &&
               Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Number, Cabinet, Group, Name);
    }
}