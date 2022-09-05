namespace StudentsTimetable.Config
{
    public record MongoConfig(
        string DbName, string Host, int Port, string AuthorizationName, string AuthorizationPassword
    );
}