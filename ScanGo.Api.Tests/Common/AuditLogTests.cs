using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ScanGo.Api.Common;
using ScanGo.Api.Features.Auth;
using ScanGo.Api.Tests.Features.Auth;

namespace ScanGo.Api.Tests.Common;

public class AuditLogTests(AuthApiFixture fx)
    : IClassFixture<AuthApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fx.ResetDbAsync();

    private static RegisterRequest GoodRegister(string email) => new()
    {
        Email = email,
        Password = "Password1",
        Name = "T",
        TermsAccepted = true,
        PrivacyAccepted = true,
    };

    [Fact]
    public async Task Register_WritesAuditEntry()
    {
        var http = fx.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("audit1@example.com")))
            .EnsureSuccessStatusCode();

        await using var db = fx.CreateDbContext();
        var user = await db.Users.FirstAsync(u => u.Email == "audit1@example.com");
        var entry = await db.AuditLog
            .Where(a => a.ActorUserId == user.Id && a.Action == AuditActions.Register)
            .FirstOrDefaultAsync();
        entry.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_WritesAuditEntry()
    {
        var http = fx.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("audit2@example.com")))
            .EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "audit2@example.com",
            Password = "Password1",
        })).EnsureSuccessStatusCode();

        await using var db = fx.CreateDbContext();
        var entries = await db.AuditLog
            .Where(a => a.Action == AuditActions.Login)
            .ToListAsync();
        entries.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task FailedLogin_WritesLoginFailedAuditEntry()
    {
        var http = fx.CreateClient();
        (await http.PostAsJsonAsync("/api/auth/register", GoodRegister("audit3@example.com")))
            .EnsureSuccessStatusCode();
        await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "audit3@example.com",
            Password = "WrongPw1",
        });

        await using var db = fx.CreateDbContext();
        var fails = await db.AuditLog
            .CountAsync(a => a.Action == AuditActions.LoginFailed);
        fails.Should().Be(1);
    }

    [Fact]
    public async Task UnknownEmailLogin_StillLoggedAsFailed()
    {
        var http = fx.CreateClient();
        await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "ghost@example.com",
            Password = "WhateverPw1",
        });

        await using var db = fx.CreateDbContext();
        var fails = await db.AuditLog
            .Where(a => a.Action == AuditActions.LoginFailed)
            .ToListAsync();
        fails.Should().HaveCount(1);
        fails[0].ActorUserId.Should().BeNull("no user matched");
    }
}
