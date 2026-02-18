using System.Buffers;
using System.Diagnostics;
using System.Text;
using TerraformDotnet.Hcl.Nodes;
using TerraformDotnet.Hcl.Writer;
using Xunit.Abstractions;

namespace TerraformDotnet.Hcl.Benchmarks;

/// <summary>
/// Baseline performance tests to detect regressions in HCL parse and emit operations.
/// Uses Stopwatch with warmup iterations and threshold assertions.
/// </summary>
public class HclBaselinePerformanceTests(ITestOutputHelper output)
{
    private const int WarmupIterations = 100;
    private const int MeasureIterations = 10_000;

    // Threshold values (milliseconds for 10,000 iterations)
    // These should be updated if intentional performance changes are made
    private const int SmallParseMaxMs = 1000;
    private const int SmallEmitMaxMs = 1000;
    private const int SmallRoundTripMaxMs = 2000;
    private const int MediumParseMaxMs = 3000;
    private const int MediumEmitMaxMs = 3000;
    private const int MediumRoundTripMaxMs = 6000;
    private const int LargeParseMaxMs = 10000;
    private const int LargeEmitMaxMs = 10000;
    private const int LargeRoundTripMaxMs = 20000;

    /// <summary>Small HCL fixture: ~10 lines, 2 attributes, 1 simple block.</summary>
    private static readonly byte[] SmallHcl = Encoding.UTF8.GetBytes("""
        region = "us-east-1"
        count  = 3

        resource "aws_instance" "web" {
          ami           = "ami-12345678"
          instance_type = "t2.micro"
        }
        """);

    /// <summary>Medium HCL fixture: ~50 lines, multiple blocks, nested blocks, expressions.</summary>
    private static readonly byte[] MediumHcl = Encoding.UTF8.GetBytes("""
        variable "name" {
          description = "The application name"
          type        = string
          default     = "myapp"
        }

        variable "environment" {
          type    = string
          default = "dev"
        }

        variable "tags" {
          type = map
          default = {
            managed_by = "terraform"
            team       = "platform"
          }
        }

        locals {
          full_name   = "app-service"
          is_prod     = true
          instance_id = 42
          subnet_ids  = ["subnet-1", "subnet-2", "subnet-3"]
        }

        resource "azurerm_resource_group" "main" {
          name     = "rg-main"
          location = "eastus"
        }

        resource "azurerm_app_service" "app" {
          name                = "app-web"
          location            = "eastus"
          resource_group_name = "rg-main"

          site_config {
            dotnet_framework_version = "v6.0"
            always_on                = true
          }

          app_settings = {
            WEBSITE_RUN_FROM_PACKAGE = "1"
            ASPNETCORE_ENVIRONMENT   = "Production"
          }
        }

        output "app_url" {
          value       = "https://app-web.azurewebsites.net"
          description = "The application URL"
        }

        output "resource_group_id" {
          value = "rg-main-id"
        }
        """);

    /// <summary>Large HCL fixture: ~100+ lines with all feature types combined.</summary>
    private static readonly byte[] LargeHcl = Encoding.UTF8.GetBytes("""
        terraform {
          required_version = ">= 1.0"

          required_providers {
            azurerm = {
              source  = "hashicorp/azurerm"
              version = "~> 3.0"
            }
            random = {
              source  = "hashicorp/random"
              version = "~> 3.0"
            }
          }

          backend "azurerm" {
            resource_group_name  = "rg-terraform-state"
            storage_account_name = "stterraformstate"
            container_name       = "tfstate"
            key                  = "prod.terraform.tfstate"
          }
        }

        provider "azurerm" {
          features {}
        }

        variable "project_name" {
          description = "Name of the project"
          type        = string
          default     = "myproject"
        }

        variable "environment" {
          description = "Deployment environment"
          type        = string
          default     = "staging"
        }

        variable "instance_count" {
          description = "Number of instances to create"
          type        = number
          default     = 3
        }

        variable "tags" {
          description = "Common tags for all resources"
          type        = map
          default = {
            managed_by  = "terraform"
            team        = "platform"
            cost_center = "engineering"
          }
        }

        variable "allowed_cidrs" {
          description = "CIDR blocks allowed to access resources"
          type        = list
          default     = ["10.0.0.0/8", "172.16.0.0/12"]
        }

        variable "enable_monitoring" {
          description = "Whether to enable monitoring"
          type        = bool
          default     = true
        }

        locals {
          full_name       = "project-main"
          normalized_env  = "staging"
          is_production   = false
          resource_prefix = "myp"
          common_tags = {
            project     = "myproject"
            environment = "staging"
            managed_by  = "terraform"
          }
        }

        resource "azurerm_resource_group" "main" {
          name     = "rg-myproject-staging"
          location = "eastus"
        }

        resource "azurerm_virtual_network" "main" {
          name                = "vnet-myproject-staging"
          location            = "eastus"
          resource_group_name = "rg-main"
          address_space       = ["10.0.0.0/16"]
        }

        resource "azurerm_subnet" "main" {
          name                 = "snet-myproject-staging"
          resource_group_name  = "rg-main"
          virtual_network_name = "vnet-main"
          address_prefixes     = ["10.0.1.0/24"]
        }

        resource "azurerm_network_security_group" "main" {
          name                = "nsg-myproject-staging"
          location            = "eastus"
          resource_group_name = "rg-main"
        }

        resource "azurerm_storage_account" "main" {
          name                     = "stmyprojectstaging"
          resource_group_name      = "rg-main"
          location                 = "eastus"
          account_tier             = "Standard"
          account_replication_type = "LRS"
          min_tls_version          = "TLS1_2"

          blob_properties {
            versioning_enabled = true

            delete_retention_policy {
              days = 30
            }
          }
        }

        resource "azurerm_key_vault" "main" {
          name                       = "kv-myproject-staging"
          location                   = "eastus"
          resource_group_name        = "rg-main"
          tenant_id                  = "00000000-0000-0000-0000-000000000000"
          sku_name                   = "standard"
          soft_delete_retention_days = 90
          purge_protection_enabled   = true
        }

        # Monitoring resources
        resource "azurerm_log_analytics_workspace" "main" {
          name                = "law-myproject-staging"
          location            = "eastus"
          resource_group_name = "rg-main"
          sku                 = "PerGB2018"
          retention_in_days   = 30
        }

        output "resource_group_name" {
          value       = "rg-myproject-staging"
          description = "The name of the resource group"
        }

        output "vnet_id" {
          value       = "vnet-main-id"
          description = "The ID of the virtual network"
        }

        output "storage_account_name" {
          value       = "stmyprojectstaging"
          description = "The name of the storage account"
        }

        output "key_vault_name" {
          value       = "kv-myproject-staging"
          description = "The name of the key vault"
        }
        """);

    // Parse benchmarks

    [Fact]
    public void Baseline_SmallParse()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            HclFile.Load(SmallHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            HclFile.Load(SmallHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Small Parse: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < SmallParseMaxMs,
            $"Small parse took {sw.ElapsedMilliseconds}ms, expected < {SmallParseMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_MediumParse()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            HclFile.Load(MediumHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            HclFile.Load(MediumHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Medium Parse: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < MediumParseMaxMs,
            $"Medium parse took {sw.ElapsedMilliseconds}ms, expected < {MediumParseMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_LargeParse()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            HclFile.Load(LargeHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            HclFile.Load(LargeHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Large Parse: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < LargeParseMaxMs,
            $"Large parse took {sw.ElapsedMilliseconds}ms, expected < {LargeParseMaxMs}ms. Performance regression detected!");
    }

    // Emit benchmarks

    [Fact]
    public void Baseline_SmallEmit()
    {
        var file = HclFile.Load(SmallHcl);

        for (int i = 0; i < WarmupIterations; i++)
        {
            EmitToBuffer(file);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            EmitToBuffer(file);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Small Emit: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < SmallEmitMaxMs,
            $"Small emit took {sw.ElapsedMilliseconds}ms, expected < {SmallEmitMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_MediumEmit()
    {
        var file = HclFile.Load(MediumHcl);

        for (int i = 0; i < WarmupIterations; i++)
        {
            EmitToBuffer(file);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            EmitToBuffer(file);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Medium Emit: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < MediumEmitMaxMs,
            $"Medium emit took {sw.ElapsedMilliseconds}ms, expected < {MediumEmitMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_LargeEmit()
    {
        var file = HclFile.Load(LargeHcl);

        for (int i = 0; i < WarmupIterations; i++)
        {
            EmitToBuffer(file);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            EmitToBuffer(file);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Large Emit: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < LargeEmitMaxMs,
            $"Large emit took {sw.ElapsedMilliseconds}ms, expected < {LargeEmitMaxMs}ms. Performance regression detected!");
    }

    // Round-trip benchmarks (parse + emit)

    [Fact]
    public void Baseline_SmallRoundTrip()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            RoundTrip(SmallHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            RoundTrip(SmallHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Small Round-Trip: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < SmallRoundTripMaxMs,
            $"Small round-trip took {sw.ElapsedMilliseconds}ms, expected < {SmallRoundTripMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_MediumRoundTrip()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            RoundTrip(MediumHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            RoundTrip(MediumHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Medium Round-Trip: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < MediumRoundTripMaxMs,
            $"Medium round-trip took {sw.ElapsedMilliseconds}ms, expected < {MediumRoundTripMaxMs}ms. Performance regression detected!");
    }

    [Fact]
    public void Baseline_LargeRoundTrip()
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            RoundTrip(LargeHcl);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MeasureIterations; i++)
        {
            RoundTrip(LargeHcl);
        }

        sw.Stop();

        var avgNs = sw.Elapsed.TotalNanoseconds / MeasureIterations;
        output.WriteLine($"Large Round-Trip: {avgNs:F0} ns/op ({sw.ElapsedMilliseconds} ms for {MeasureIterations} iterations)");

        Assert.True(sw.ElapsedMilliseconds < LargeRoundTripMaxMs,
            $"Large round-trip took {sw.ElapsedMilliseconds}ms, expected < {LargeRoundTripMaxMs}ms. Performance regression detected!");
    }

    /// <summary>Emits an HCL file AST to a pooled buffer and returns the byte count.</summary>
    private static int EmitToBuffer(HclFile file)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using var writer = new Utf8HclWriter(buffer);
        var emitter = new HclFileEmitter(writer);
        emitter.Emit(file);
        writer.Flush();

        return buffer.WrittenCount;
    }

    /// <summary>Parses HCL bytes and emits back to a buffer (full round-trip).</summary>
    private static void RoundTrip(byte[] hcl)
    {
        var file = HclFile.Load(hcl);
        EmitToBuffer(file);
    }
}
