import 'package:sqflite/sqflite.dart';
import 'package:path/path.dart';
import 'package:flutter/foundation.dart';
import 'dart:convert';
import '../../../domain/entities/order_item.dart';

class LocalJournalDatabase {
  static Database? _db;

  Future<Database> get database async {
    if (_db != null) return _db!;
    _db = await _initDB('journal.db');
    return _db!;
  }

  Future<Database> _initDB(String fileName) async {
    final dbPath = await getDatabasesPath();
    final path = join(dbPath, fileName);

    return await openDatabase(
      path,
      version: 1,
      onCreate: _createDB,
    );
  }

  Future<void> _createDB(Database db, int version) async {
    await db.execute('''
      CREATE TABLE orders (
        id TEXT PRIMARY KEY,
        timestamp TEXT NOT null,
        payload JSON NOT null,
        synced INTEGER NOT null
      )
    ''');
  }

  Future<void> saveTransaction(String orderId, List<OrderItem> items) async {
    try {
      final db = await database;
      
      final itemsMap = items.map((i) => {
        'id': i.id,
        'productId': i.productId,
        'unitPriceCents': i.unitPriceCents,
        'quantity': i.quantity,
      }).toList();

      await db.insert(
        'orders',
        {
          'id': orderId,
          'timestamp': DateTime.now().toIso8601String(),
          'payload': jsonEncode(itemsMap),
          'synced': 0
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    } catch (e) {
      debugPrint('DB Error: $e');
    }
  }
}
