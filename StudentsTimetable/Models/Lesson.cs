namespace StudentsTimetable.Models;

public class Lesson
{
    public int Number { get; set; }
    public string Cabinet { get; set; }
    public string Group { get; set; }
    public string Name { get; set; }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            
            hash = hash * 31 + this.Number.GetHashCode();
            hash = hash * 31 + this.Cabinet.GetHashCode();
            hash = hash * 31 + this.Name.GetHashCode();

            return hash;
        }
    }
}