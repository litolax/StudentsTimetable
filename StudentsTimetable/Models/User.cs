﻿using MongoDB.Bson;

namespace StudentsTimetable.Models;

public class User
{
    public ObjectId Id { get; set; }
    public long UserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string?[]? Groups { get; set; }
    public bool Notifications { get; set; }

    public User(long userId, string? username, string firstName, string? lastName)
    {
        this.UserId = userId;
        this.Username = username;
        this.FirstName = firstName;
        this.LastName = lastName;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj switch
        {
            null => false,
            User user => this.Id == user.Id && this.UserId == user.UserId,
            _ => false
        };
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, UserId);
    }
}