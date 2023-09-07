namespace StudentsTimetable.Models;

public class Lesson
{
    public int Number { get; set; }
    public string Cabinet { get; set; }
    public string Group { get; set; }
    public string Name { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj == null || this.GetType() != obj.GetType() || obj is not Lesson other)
        {
            return false;
        }

        return this.Number == other.Number && this.Cabinet == other.Cabinet && this.Group == other.Group && this.Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Number, this.Cabinet, this.Group, this.Name);
    }
}