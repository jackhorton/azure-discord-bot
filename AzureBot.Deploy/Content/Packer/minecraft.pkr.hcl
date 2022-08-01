variable "subscription_id" {}
variable "location" {}
variable "identity_resource_id" {}
variable "destination_resource_group" {}
variable "destination_gallery_name" {}
variable "destination_image_name" {}
variable "destination_image_version" {}
variable "destination_replication_regions" {
  type = list(string)
  default = []
}

source "azure-arm" "minecraft" {
  subscription_id = var.subscription_id
  location        = var.location
  vm_size         = "Standard_D2as_v5"
  user_assigned_managed_identities = [
    var.identity_resource_id
  ]

  os_type         = "Linux"
  image_publisher = "Canonical"
  image_offer     = "0001-com-ubuntu-server-focal"
  image_sku       = "20_04-lts"

  shared_image_gallery_destination {
    subscription         = var.subscription_id
    resource_group       = var.destination_resource_group
    gallery_name         = var.destination_gallery_name
    image_name           = var.destination_image_name
    image_version        = var.destination_image_version
    replication_regions  = var.destination_replication_regions
    storage_account_type = "Standard_LRS"
  }
  managed_image_name = "${var.destination_image_name}-${var.destination_image_version}"
  managed_image_resource_group_name = var.destination_resource_group
}

build {
  name = "minecraft"
  sources = [
    "source.azure-arm.minecraft"
  ]

  provisioner "shell" {
    inline = [
      "cloud-init status --wait",
    ]
  }

  provisioner "shell" {
    env = {
      "DEBIAN_FRONTEND" = "noninteractive"
    }
    execute_command = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E sh '{{ .Path }}'"
    inline = [
      "apt-get update",
      "apt-get upgrade -y",
      "apt-get install -y openjdk-17-jdk-headless",
    ]
  }

  provisioner "shell" {
    execute_command = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E sh '{{ .Path }}'"
    inline = [
      "/usr/sbin/waagent -force -deprovision+user && export HISTSIZE=0 && sync"
    ]
    inline_shebang = "/bin/sh -x"
  }
}