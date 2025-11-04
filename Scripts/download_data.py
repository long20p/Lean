import os
import sys
import zipfile
import pandas as pd
import yfinance as yf
from datetime import datetime

# === HOW TO USE ===
# python download_data.py AAPL 2023-01-01 2023-12-31
# ==================

MULTIPLIER = 10000

def convert_price_value(val):
    return int(float(val) * MULTIPLIER)

def main():
    if len(sys.argv) < 4:
        print("Usage: python download_data.py <symbol> <start_date> <end_date>")
        print("Example: python download_data.py AAPL 2023-01-01 2023-12-31")
        sys.exit(1)

    symbol = sys.argv[1].upper()
    start_date = sys.argv[2]
    end_date = sys.argv[3]

    lean_data_dir = r"C:\GitRepo\Finance\LeanFork\Data"

    print(f"Downloading {symbol} data from Yahoo Finance ({start_date} → {end_date})...")
    data = yf.download(symbol, start=start_date, end=end_date, auto_adjust=True)

    if data.empty:
        print(f"No data found for {symbol} in this date range.")
        sys.exit(1)

    # Format for LEAN: yyyyMMdd HH:mm,open,high,low,close,volume
    formatted_data = []
    for date, row in data.iterrows():
        dt = date.strftime("%Y%m%d 00:00")
        open_val = convert_price_value(row['Open'].iloc[0])
        high_val = convert_price_value(row['High'].iloc[0])
        low_val = convert_price_value(row['Low'].iloc[0])
        close_val = convert_price_value(row['Close'].iloc[0])
        volume_val = int(row['Volume'].iloc[0])
        formatted_data.append(f"{dt},{open_val},{high_val},{low_val},{close_val},{volume_val}")

    symbol_lower = symbol.lower()
    output_dir = os.path.join(lean_data_dir, "equity", "usa", "daily")
    print(f"Saving data to {output_dir}...")
    os.makedirs(output_dir, exist_ok=True)
    csv_path = os.path.join(output_dir, f"{symbol_lower}.csv")
    zip_path = os.path.join(output_dir, f"{symbol_lower}.zip")

    # Save CSV and zip it
    with open(csv_path, "w") as f:
        f.write("\n".join(formatted_data))

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        z.write(csv_path, arcname=f"{symbol_lower}.csv")

    os.remove(csv_path)  # optional cleanup

    print(f"\nData ready for LEAN: {zip_path}")
    print(f"Contains {len(formatted_data)} daily bars for {symbol}.")

if __name__ == "__main__":
    main()
