using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using NeoAdapter.Domain;
using Renci.SshNet;
using Xunit;

namespace NeoAdapter.IntegrationTests;

public class SftpConnectorTests : IAsyncLifetime
{
    // Spins up a real, lightweight SFTP server in Docker
    private readonly IContainer _sftpContainer = new ContainerBuilder()
        .WithImage("atmoz/sftp:latest")
        .WithPortBinding(22, true)
        .WithCommand("foo:pass:::upload") // Creates user "foo", password "pass", and directory "/home/foo/upload"
        .Build();

    private readonly Mock<ISqlSecretProtector> _secretProtectorMock = new();
    private string _tempLocalDir = string.Empty;

    public async Task InitializeAsync()
    {
        await _sftpContainer.StartAsync();
        _secretProtectorMock.Setup(x => x.Protect(It.IsAny<string>())).Returns<string>(x => x);
        _secretProtectorMock.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(x => x);
        
        _tempLocalDir = Path.Combine(Path.GetTempPath(), $"neoadapter_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempLocalDir);

        // Wait for SFTP server to be fully ready
        var port = _sftpContainer.GetMappedPublicPort(22);
        var host = _sftpContainer.Hostname;
        int maxRetries = 20;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var client = new SftpClient(host, port, "foo", "pass");
                client.Connect();
                if (client.IsConnected)
                {
                    client.Disconnect();
                    break;
                }
            }
            catch
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(500);
            }
        }
    }

    public async Task DisposeAsync()
    {
        await _sftpContainer.DisposeAsync();
        if (Directory.Exists(_tempLocalDir))
        {
            try
            {
                Directory.Delete(_tempLocalDir, true);
            }
            catch {}
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferFilesFromLocalPathToSftp()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Create source dummy files locally
        var file1 = Path.Combine(_tempLocalDir, "test1.txt");
        var file2 = Path.Combine(_tempLocalDir, "test2.txt");
        await File.WriteAllTextAsync(file1, "Hello from file 1");
        await File.WriteAllTextAsync(file2, "Hello from file 2");

        // 2. Setup SFTP settings mapping to docker container
        var hostname = _sftpContainer.Hostname;
        var port = _sftpContainer.GetMappedPublicPort(22);

        var pathConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Local Path Source",
            Type = ConnectorType.Path,
            LocalPath = _tempLocalDir
        };

        var sftpConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "SFTP Destination",
            Type = ConnectorType.Sftp,
            SftpHost = hostname,
            SftpPort = port,
            SftpUsername = "foo",
            SftpPassword = "pass",
            SftpRemotePath = "/upload" // User "foo" chroot jail is /home/foo, so upload folder is at root/upload
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "Local to SFTP Job",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        job.Steps.Add(new IntegrationJobStep
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = job.Id,
            OrderIndex = 0,
            SourceConnectorId = pathConnector.Id,
            DestinationConnectorId = sftpConnector.Id,
            SourceConnector = pathConnector,
            DestinationConnector = sftpConnector
        });

        dbContext.Connectors.AddRange(pathConnector, sftpConnector);
        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var executor = new IntegrationJobExecutor(dbContext, _secretProtectorMock.Object);

        // Act
        await executor.ExecuteAsync(job.Id);

        // Assert: Connect to SFTP manually to verify files were uploaded
        using var sftpClient = new SftpClient(hostname, port, "foo", "pass");
        sftpClient.Connect();
        sftpClient.IsConnected.Should().BeTrue();

        var uploadedFiles = sftpClient.ListDirectory("/upload").ToList();
        var fileNames = uploadedFiles.Select(f => f.Name).ToList();

        fileNames.Should().Contain("test1.txt");
        fileNames.Should().Contain("test2.txt");

        sftpClient.Disconnect();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTransferFilesFromSftpToLocalPath()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new NeoAdapterDbContext(options);

        // 1. Upload source dummy files to SFTP container first
        var hostname = _sftpContainer.Hostname;
        var port = _sftpContainer.GetMappedPublicPort(22);

        using (var sftpClient = new SftpClient(hostname, port, "foo", "pass"))
        {
            sftpClient.Connect();
            
            // Write two files to SFTP remote upload directory
            using (var ms1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Data on remote 1")))
            {
                sftpClient.UploadFile(ms1, "/upload/remote1.data");
            }
            using (var ms2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Data on remote 2")))
            {
                sftpClient.UploadFile(ms2, "/upload/remote2.data");
            }
            
            sftpClient.Disconnect();
        }

        // 2. Setup Connectors
        var sftpConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "SFTP Source",
            Type = ConnectorType.Sftp,
            SftpHost = hostname,
            SftpPort = port,
            SftpUsername = "foo",
            SftpPassword = "pass",
            SftpRemotePath = "/upload"
        };

        var pathConnector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = "Local Path Destination",
            Type = ConnectorType.Path,
            LocalPath = _tempLocalDir
        };

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = "SFTP to Local Job",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        job.Steps.Add(new IntegrationJobStep
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = job.Id,
            OrderIndex = 0,
            SourceConnectorId = sftpConnector.Id,
            DestinationConnectorId = pathConnector.Id,
            SourceConnector = sftpConnector,
            DestinationConnector = pathConnector
        });

        dbContext.Connectors.AddRange(sftpConnector, pathConnector);
        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var executor = new IntegrationJobExecutor(dbContext, _secretProtectorMock.Object);

        // Act
        await executor.ExecuteAsync(job.Id);

        // Assert: Local temp directory should contain the files
        var localFiles = Directory.GetFiles(_tempLocalDir).Select(Path.GetFileName).ToList();
        localFiles.Should().Contain("remote1.data");
        localFiles.Should().Contain("remote2.data");

        var content1 = await File.ReadAllTextAsync(Path.Combine(_tempLocalDir, "remote1.data"));
        content1.Should().Be("Data on remote 1");

        var content2 = await File.ReadAllTextAsync(Path.Combine(_tempLocalDir, "remote2.data"));
        content2.Should().Be("Data on remote 2");
    }
}
