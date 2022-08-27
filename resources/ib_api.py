import os
import sys
from ib_insync import *
import setproctitle

module_name = os.path.basename(__file__)
setproctitle.setproctitle(module_name)


ib = IB()
clientId = 1


def orderFilled(trade, fill):
    print("trade: " + trade)
    print("filled: " + fill)


# this is an async operation that just sends the order, it provides no execution confirmation
def place_order(
    order_type: str,
    ticker: str,
    quantity: int,
    price: float,
    direction: str,
    is_paper_account: str,
):
    port = 7497 if is_paper_account == "True" else 7496
    ib.disconnect()
    ib.connect("127.0.0.1", port, clientId=clientId)
    ib.sleep(1)

    side = "BUY" if direction == "True" else "SELL"

    stock = Stock(ticker.upper(), "SMART", "USD")
    contract_detail = ib.reqContractDetails(stock)
    if len(contract_detail) > 1:
        raise Exception(
            f'"ticker" {ticker.upper()} is ambiguous, it has more than one contract definition, order cannot be safely placed.'
        )

    match (order_type.upper()):
        case "LIMIT":
            order = LimitOrder(side, quantity, price, tif="GTC")
        case "MARKET":
            order = MarketOrder(side, quantity)

        case _:
            raise Exception(f'"order_type" --> "{order_type}" is not valid.')

    trade = ib.placeOrder(contract_detail[0].contract, order)
    # trade.filledEvent += orderFilled
    return trade


def get_portfolio(is_paper_account: str, write_to_csv: str):
    port = 7497 if is_paper_account == "True" else 7496
    ib.disconnect()
    ib.connect("127.0.0.1", port, clientId=clientId)
    ib.sleep(1)

    pf = ib.portfolio()
    if bool(write_to_csv):
        with open(f"portfolio.csv", "w") as f:
            f.write("ticker,open_position,average_cost,market_price,unrealized_pnl\n")
            for s in pf:
                f.write(s.contract.localSymbol + ",")
                f.write(str(s.position) + ",")
                f.write(str(s.averageCost) + ",")
                f.write(str(s.marketPrice) + ",")
                f.write(str(s.unrealizedPNL) + "\n")
    return pf


def get_open_orders(is_paper_account: str, write_to_csv: str):
    port = 7497 if is_paper_account == "True" else 7496
    ib.disconnect()
    ib.connect("127.0.0.1", port, clientId=clientId)
    ib.sleep(1)

    ### It is not clear why in order to receive the trades (ib.trades()) from the API
    #   ib.reqAllOpenOrders() has to be lauched, most likely a quirk in the library
    ###
    ib.reqAllOpenOrders()
    trades = ib.trades()
    if bool(write_to_csv):
        with open(f"open_orders.csv", "w") as f:
            f.write("ticker,open_quantity,price_level,direction\n")
            for s in trades:
                if s.orderStatus.remaining == 0:
                    continue
                f.write(s.contract.localSymbol + ",")
                f.write(str(s.orderStatus.remaining) + ",")
                f.write(str(s.order.lmtPrice) + ",")
                f.write(("true" if s.order.action == "BUY" else "false") + "\n")
    return trades


def cancel_order(
    ticker: str,
    price: str,
    direction: str,
    is_paper_account: str,
):
    port = 7497 if is_paper_account == "True" else 7496
    ib.disconnect()
    ib.connect("127.0.0.1", port, clientId=clientId)
    ib.sleep(1)

    open_orders = get_open_orders(is_paper_account, write_to_csv=False)
    action_filter = "BUY" if direction == "True" else "SELL"
    order_type_filter = "MKT" if price == "0" else "LMT"
    order_ids = [
        order.order
        for order in open_orders
        if (
            ticker.upper() == order.contract.localSymbol
            and (order.order.action == action_filter)
            and (order.order.orderType == order_type_filter)
            and (
                float(price) == order.order.lmtPrice
                if order_type_filter == "LMT"
                else True
            )
            # and float(quantity) == order.orderStatus.remaining
        )
    ]

    if len(order_ids) == 0:
        raise Exception(
            f"no open order found for given characteristics,\nticker:{ticker.upper()}, direction:{direction}, price:{price}."
        )

    ordersCancelled = []
    for id in order_ids:
        # a client can only modify orders that it has created itself
        if id.clientId != clientId:
            continue
        ib.disconnect()
        ib.connect("127.0.0.1", port, clientId=clientId)
        ib.sleep(1)
        trade = ib.cancelOrder(id)
        ordersCancelled.append(trade)
        break  # if there are multiple identical orders, cancel only one

    return ordersCancelled


def get_buying_power(is_paper_account: str, write_to_csv: str):
    port = 7497 if is_paper_account == "True" else 7496
    ib.disconnect()
    ib.connect("127.0.0.1", port, clientId=clientId)
    ib.sleep(1)

    accountValues = ib.accountValues()

    # this will return the buying power - 1, which is fine
    buying_power = -1
    for av in accountValues:
        if av.tag == "BuyingPower":
            buying_power += float(av.value)
            break

    if buying_power == -1:
        raise Exception(
            f"accounts buying power cannot be% -1, please run a manual check."
        )

    if bool(write_to_csv):
        with open(f"buying_power.csv", "w") as f:
            f.write("buying_power\n")
            f.write(str(buying_power) + "\n")

    return buying_power


if __name__ == "__main__":
    # test methods
    # place_order("LIMIT", "NFLX", 1, 227.75, "True", "True")
    # place_order("MARKET", "META", 1, 227.75, "True", "True")
    # get_contract_details("META", "True", "True")
    # cancel_order("META", "0", "False", "True")
    # get_open_orders("False", "True")
    # get_available_balance("False", "True")
    # get_portfolio("True", "True")

    match (sys.argv[1]):
        case "get_portfolio":
            get_portfolio(sys.argv[2], sys.argv[3])
        case "place_order":
            place_order(
                sys.argv[2],
                sys.argv[3],
                sys.argv[4],
                sys.argv[5],
                sys.argv[6],
                sys.argv[7],
            )
        case "get_open_orders":
            get_open_orders(sys.argv[2], sys.argv[3])
        case "cancel_order":
            cancel_order(
                sys.argv[2],
                sys.argv[3],
                sys.argv[4],
                sys.argv[5],
            )
        case "buying_power":
            get_buying_power(sys.argv[2], sys.argv[3])
        case _:
            raise Exception(f'method called "{sys.argv[1]}" is not valid')

    ib.sleep(1)
    ib.disconnect()

    # ib.run()
