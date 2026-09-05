# 文档总览

本目录按“使用、集成、维护”组织。协议和存储不再拆成大量单页，相关内容集中在主题文档中，便于搜索、复制和更新。

## 使用 SDK

| 需求 | 文档 |
| --- | --- |
| 了解模块边界、命名空间、配置和运行时 | [架构与配置](architecture.md) |
| 复制单协议 Options + Client 示例 | [协议参考](protocols.md) |
| 从控制台接入 ADS，读取基础类型、字符串、时间和数组 | [ADS 控制台实战](protocols.md#ads-控制台实战) |
| 接入 SQL Server 或 MySQL 历史库 | [历史存储](storage.md) |
| 接入 MES、HTTP、MQTT Broker、WebSocket、FTP | [集成能力](integrations.md) |
| 在没有实体 PLC 时验证 S7 Demo | [Snap7Server 本地虚拟 PLC](snap7-server.md) |

## 维护项目

| 需求 | 文档 |
| --- | --- |
| 查看稳定性设计、协议审查和验证边界 | [工程记录](engineering-notes.md) |
| 查看构建、测试和 Demo | [根目录 README](../README.md) |

## 文档约定

- 根目录 README 只保留项目定位、快速开始和入口链接。
- `architecture.md` 记录当前设计，不记录易过期的 Git 提交号。
- `protocols.md` 和 `storage.md` 以可复制示例为主；示例地址、账号和密码都是占位值。
- `engineering-notes.md` 区分“已完成”“待验证”和“后续建议”，避免把历史建议误读为当前功能。
- 代码、配置和现场观察优先于文档中的旧示例；当公开 API 变化时，先更新主题文档再更新入口。
